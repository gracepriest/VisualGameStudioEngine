using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.IR
{
    /// <summary>
    /// Builds IR from AST with SSA transformation
    /// </summary>
    public class IRBuilder : IASTVisitor
    {
        private readonly SemanticAnalyzer _semanticAnalyzer;
        private IRModule _module;
        private IRFunction _currentFunction;
        private BasicBlock _currentBlock;
        private readonly Stack<LoopContext> _loopStack;
        private readonly Dictionary<string, Stack<IRVariable>> _variableVersions;
        private readonly Dictionary<string, IRVariable> _globalVariables;
        private readonly Dictionary<string, IRAlloca> _locals;
        private string _currentClassName;
        private string _currentNamespace;
        private List<IRValue> _pendingBaseConstructorArgs;  // Temporary storage for base constructor args

        // For SSA construction
        private int _nextVersion = 0;

        public IRModule Module => _module;

        public IRBuilder(SemanticAnalyzer semanticAnalyzer)
        {
            _semanticAnalyzer = semanticAnalyzer;
            _loopStack = new Stack<LoopContext>();
            _variableVersions = new Dictionary<string, Stack<IRVariable>>();
            _globalVariables = new Dictionary<string, IRVariable>();
            _locals = new Dictionary<string, IRAlloca>();
        }

        /// <summary>
        /// Build IR from program AST
        /// </summary>
        public IRModule Build(ProgramNode program, string moduleName = "main")
        {
            _module = new IRModule(moduleName);
            _currentFunction = null;
            _currentBlock = null;

            program.Accept(this);

            return _module;
        }

        private IRVariable CreateVariable(string name, TypeInfo type, int version = 0)
        {
            return new IRVariable(name, type, version);
        }

        private IRVariable GetOrCreateVariable(string name, TypeInfo type)
        {
            // Check for existing version
            if (_variableVersions.ContainsKey(name) && _variableVersions[name].Count > 0)
            {
                return _variableVersions[name].Peek();
            }

            // Check global
            if (_globalVariables.ContainsKey(name))
            {
                return _globalVariables[name];
            }

            // Create new version
            var variable = CreateVariable(name, type, _nextVersion++);

            if (!_variableVersions.ContainsKey(name))
            {
                _variableVersions[name] = new Stack<IRVariable>();
            }

            _variableVersions[name].Push(variable);

            return variable;
        }

        private void PushVariableVersion(string name, IRVariable variable)
        {
            if (!_variableVersions.ContainsKey(name))
            {
                _variableVersions[name] = new Stack<IRVariable>();
            }
            _variableVersions[name].Push(variable);
        }

        private void PopVariableVersion(string name)
        {
            if (_variableVersions.ContainsKey(name) && _variableVersions[name].Count > 0)
            {
                _variableVersions[name].Pop();
            }
        }

        private void EmitInstruction(IRInstruction instruction)
        {
            if (_currentBlock != null)
            {
                _currentBlock.AddInstruction(instruction);
            }
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

        public void Visit(NamespaceNode node)
        {
            // Save current namespace
            var savedNamespace = _currentNamespace;

            // Set current namespace (support nested namespaces)
            _currentNamespace = _currentNamespace != null
                ? $"{_currentNamespace}.{node.Name}"
                : node.Name;

            // Track namespace in module
            if (!_module.Namespaces.Contains(_currentNamespace))
            {
                _module.Namespaces.Add(_currentNamespace);
            }

            // Process members
            foreach (var member in node.Members)
            {
                member.Accept(this);
            }

            // Restore namespace
            _currentNamespace = savedNamespace;
        }

        public void Visit(ModuleNode node)
        {
            // Modules are organizational - process members
            foreach (var member in node.Members)
            {
                member.Accept(this);
            }
        }

        public void Visit(UsingDirectiveNode node)
        {
            // If this is a .NET namespace, add it to the module's NetUsings
            if (node.IsNetNamespace)
            {
                _module.NetUsings.Add(new NetUsingDirective(node.Namespace, node.Alias));
            }
            // BasicLang module usings don't need IR generation
        }

        public void Visit(ImportDirectiveNode node)
        {
            // No IR generation needed
        }

        // ====================================================================
        // Functions and Subroutines
        // ====================================================================

        public void Visit(FunctionNode node)
        {
            var returnType = _semanticAnalyzer.GetNodeType(node) ?? new TypeInfo("Void", TypeKind.Void);

            _currentFunction = _module.CreateFunction(node.Name, returnType);

            // Set async/iterator flags
            _currentFunction.IsAsync = node.IsAsync;
            _currentFunction.IsIterator = node.IsIterator;

            // Copy generic parameters and constraints
            foreach (var genericParam in node.GenericParameters)
            {
                _currentFunction.GenericParameters.Add(genericParam);
            }
            if (node.GenericTypeParams != null)
            {
                foreach (var typeParam in node.GenericTypeParams)
                {
                    _currentFunction.GenericTypeParams.Add(typeParam);
                }
            }

            // Create parameters
            foreach (var param in node.Parameters)
            {
                var paramType = _semanticAnalyzer.GetNodeType(param);
                var irParam = new IRVariable(param.Name, paramType)
                {
                    IsParameter = true,
                    IsOptional = param.IsOptional,
                    IsParamArray = param.IsParamArray,
                    IsByRef = param.IsByRef,
                    DefaultValue = BuildExpressionValue(param.DefaultValue)
                };
                _currentFunction.Parameters.Add(irParam);
                PushVariableVersion(param.Name, irParam);
            }

            // Create entry block
            _currentBlock = _currentFunction.CreateBlock("entry");

            // Process body
            if (node.Body != null)
            {
                node.Body.Accept(this);
            }

            // Ensure function ends with return
            if (!_currentBlock.IsTerminated())
            {
                if (returnType.Name == "Void")
                {
                    EmitInstruction(new IRReturn());
                }
                else
                {
                    // Return default value
                    var defaultValue = CreateDefaultValue(returnType);
                    EmitInstruction(new IRReturn(defaultValue));
                }
            }

            // Clean up variable versions
            foreach (var param in node.Parameters)
            {
                PopVariableVersion(param.Name);
            }

            _currentFunction = null;
            _currentBlock = null;
        }

        public void Visit(SubroutineNode node)
        {
            var voidType = new TypeInfo("Void", TypeKind.Void);

            _currentFunction = _module.CreateFunction(node.Name, voidType);

            // Set async flag
            _currentFunction.IsAsync = node.IsAsync;

            // Copy generic parameters and constraints
            foreach (var genericParam in node.GenericParameters)
            {
                _currentFunction.GenericParameters.Add(genericParam);
            }
            if (node.GenericTypeParams != null)
            {
                foreach (var typeParam in node.GenericTypeParams)
                {
                    _currentFunction.GenericTypeParams.Add(typeParam);
                }
            }

            // Create parameters
            foreach (var param in node.Parameters)
            {
                var paramType = _semanticAnalyzer.GetNodeType(param);
                var irParam = new IRVariable(param.Name, paramType)
                {
                    IsParameter = true,
                    IsOptional = param.IsOptional,
                    IsParamArray = param.IsParamArray,
                    IsByRef = param.IsByRef,
                    DefaultValue = BuildExpressionValue(param.DefaultValue)
                };
                _currentFunction.Parameters.Add(irParam);
                PushVariableVersion(param.Name, irParam);
            }

            // Create entry block
            _currentBlock = _currentFunction.CreateBlock("entry");

            // Process body
            if (node.Body != null)
            {
                node.Body.Accept(this);
            }

            // Ensure function ends with return
            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRReturn());
            }

            // Clean up variable versions
            foreach (var param in node.Parameters)
            {
                PopVariableVersion(param.Name);
            }

            _currentFunction = null;
            _currentBlock = null;
        }

        public void Visit(ParameterNode node)
        {
            // Handled in function visit
        }

        // ====================================================================
        // Declarations
        // ====================================================================

        public void Visit(VariableDeclarationNode node)
        {
            var varType = _semanticAnalyzer.GetNodeType(node) ?? new TypeInfo("Object", TypeKind.Class);

            if (_currentFunction == null)
            {
                // Global variable
                var globalVar = _module.CreateGlobalVariable(node.Name, varType);
                _globalVariables[node.Name] = globalVar;

                if (node.Initializer != null)
                {
                    // Evaluate the initializer and store it
                    node.Initializer.Accept(this);
                    globalVar.InitialValue = _expressionResult;
                }
            }
            else
            {
                // Local variable - register it for declaration
                var localVar = CreateVariable(node.Name, varType, _nextVersion++);
                PushVariableVersion(node.Name, localVar);
                _currentFunction.LocalVariables.Add(localVar);

                // Only emit IRAlloca for variables that need memory semantics
                // (arrays, ByRef parameters, address-of operations)
                bool needsMemory = varType.Kind == TypeKind.Array;

                if (needsMemory)
                {
                    var alloca = new IRAlloca($"{node.Name}_addr", varType);
                    EmitInstruction(alloca);
                }

                // Initialize if there's an initializer
                if (node.Initializer != null)
                {
                    node.Initializer.Accept(this);
                    var initValue = _expressionResult;

                    // For memory-backed variables, emit a store
                    if (needsMemory)
                    {
                        var alloca = _currentBlock.Instructions
                            .OfType<IRAlloca>()
                            .LastOrDefault(a => a.Name == $"{node.Name}_addr");
                        if (alloca != null)
                        {
                            EmitInstruction(new IRStore(initValue, alloca));
                        }
                    }

                    // Optimization: If the value is a direct call or op result,
                    // rename it to the variable instead of creating a separate assignment
                    if (initValue is IRCall call)
                    {
                        call.Name = localVar.Name;
                    }
                    else if (initValue is IRBinaryOp binOp)
                    {
                        binOp.Name = localVar.Name;
                    }
                    else if (initValue is IRUnaryOp unaryOp)
                    {
                        unaryOp.Name = localVar.Name;
                    }
                    else if (initValue is IRCompare compare)
                    {
                        compare.Name = localVar.Name;
                    }
                    else
                    {
                        // For constants, variables, or other values, emit an assignment
                        EmitInstruction(new IRAssignment(localVar, initValue));
                    }
                }
                // No initializer - C# backend will use default(T), no IR needed
            }
        }

        public void Visit(TupleDeconstructionNode node)
        {
            // Evaluate the tuple expression
            node.Initializer.Accept(this);
            var tupleValue = _expressionResult;

            // Get the tuple type
            var tupleType = _semanticAnalyzer.GetNodeType(node.Initializer);

            // Create local variables for each element and emit deconstruction
            for (int i = 0; i < node.Variables.Count; i++)
            {
                var (varName, varTypeRef) = node.Variables[i];

                // Determine the type - from explicit type or inferred from tuple
                TypeInfo varType;
                if (varTypeRef != null)
                {
                    varType = new TypeInfo(varTypeRef.Name, TypeKind.Primitive);
                }
                else if (tupleType?.TupleElementTypes != null && i < tupleType.TupleElementTypes.Count)
                {
                    varType = tupleType.TupleElementTypes[i];
                }
                else
                {
                    varType = new TypeInfo("Object", TypeKind.Class);
                }

                // Create the local variable
                var localVar = CreateVariable(varName, varType, _nextVersion++);
                PushVariableVersion(varName, localVar);
                _currentFunction.LocalVariables.Add(localVar);

                // Emit instruction to get tuple element
                var elementAccess = new IRTupleElement(tupleValue, i, varType)
                {
                    Name = localVar.Name
                };
                EmitInstruction(elementAccess);
            }
        }

        public void Visit(ConstantDeclarationNode node)
        {
            // Store constants as global variables with IsConst = true
            if (node.Value != null)
            {
                // Evaluate the constant value
                node.Value.Accept(this);
                var value = _expressionResult;

                // Resolve the type
                var typeName = node.Type?.Name ?? "Integer";
                var typeInfo = _semanticAnalyzer.GetNodeType(node) ?? new TypeInfo(typeName, TypeKind.Primitive);

                // Create the constant as a global variable
                var constVar = new IRVariable(node.Name, typeInfo)
                {
                    IsGlobal = true,
                    IsConst = true,
                    InitialValue = value
                };

                // Add to module's global variables
                if (_module != null && !_module.GlobalVariables.ContainsKey(node.Name))
                {
                    _module.GlobalVariables[node.Name] = constVar;
                }
            }
        }

        public void Visit(TypeDefineNode node)
        {
            // Type aliases don't generate IR
        }

        // ====================================================================
        // Classes and Types
        // ====================================================================

        public void Visit(ClassNode node)
        {
            // Create IR class structure
            var irClass = new IRClass(node.Name)
            {
                BaseClass = node.BaseClass,
                Namespace = _currentNamespace,
                IsAbstract = node.IsAbstract
            };

            // Copy generic parameters and constraints
            foreach (var genericParam in node.GenericParameters)
            {
                irClass.GenericParameters.Add(genericParam);
            }
            if (node.GenericTypeParams != null)
            {
                foreach (var typeParam in node.GenericTypeParams)
                {
                    irClass.GenericTypeParams.Add(typeParam);
                }
            }

            foreach (var iface in node.Interfaces)
            {
                irClass.Interfaces.Add(iface);
            }

            _module.Classes[node.Name] = irClass;
            _currentClassName = node.Name;

            // Process members - they will populate the IRClass
            foreach (var member in node.Members)
            {
                if (member is VariableDeclarationNode varDecl)
                {
                    // Add as field
                    var field = new IRField
                    {
                        Name = varDecl.Name,
                        Type = _semanticAnalyzer.GetNodeType(varDecl),
                        Access = MapAccessModifier(varDecl.Access),
                        IsStatic = varDecl.IsStatic
                    };
                    irClass.Fields.Add(field);
                }
                else if (member is FunctionNode funcNode)
                {
                    // Process function and add as method
                    member.Accept(this);

                    var method = new IRMethod
                    {
                        Name = funcNode.Name,
                        ReturnType = _semanticAnalyzer.GetNodeType(funcNode),
                        Access = MapAccessModifier(funcNode.Access),
                        IsStatic = funcNode.IsStatic,
                        IsVirtual = funcNode.IsVirtual,
                        IsOverride = funcNode.IsOverride,
                        IsAbstract = funcNode.IsAbstract,
                        IsSealed = funcNode.IsSealed,
                        Implementation = _module.Functions.LastOrDefault()
                    };
                    // Copy generic parameters
                    foreach (var genericParam in funcNode.GenericParameters)
                    {
                        method.GenericParameters.Add(genericParam);
                    }
                    irClass.Methods.Add(method);
                }
                else if (member is SubroutineNode subNode)
                {
                    // Process subroutine and add as method
                    member.Accept(this);

                    var method = new IRMethod
                    {
                        Name = subNode.Name,
                        ReturnType = new TypeInfo("Void", TypeKind.Void),
                        Access = MapAccessModifier(subNode.Access),
                        IsStatic = subNode.IsStatic,
                        IsVirtual = subNode.IsVirtual,
                        IsOverride = subNode.IsOverride,
                        IsAbstract = subNode.IsAbstract,
                        IsSealed = subNode.IsSealed,
                        Implementation = _module.Functions.LastOrDefault()
                    };
                    // Copy generic parameters
                    foreach (var genericParam in subNode.GenericParameters)
                    {
                        method.GenericParameters.Add(genericParam);
                    }
                    irClass.Methods.Add(method);
                }
                else if (member is ConstructorNode ctorNode)
                {
                    // Process constructor - this also processes base constructor args
                    _pendingBaseConstructorArgs = null;
                    member.Accept(this);

                    var ctor = new IRConstructor
                    {
                        Access = MapAccessModifier(ctorNode.Access),
                        Implementation = _module.Functions.LastOrDefault()
                    };

                    // Use the base constructor args collected during constructor processing
                    if (_pendingBaseConstructorArgs != null)
                    {
                        ctor.BaseConstructorArgs.AddRange(_pendingBaseConstructorArgs);
                    }
                    _pendingBaseConstructorArgs = null;

                    irClass.Constructors.Add(ctor);
                }
                else if (member is PropertyNode propNode)
                {
                    // Process property
                    var prop = new IRProperty
                    {
                        Name = propNode.Name,
                        Type = propNode.PropertyType != null
                            ? _semanticAnalyzer.GetNodeType(propNode)
                            : new TypeInfo("Object", TypeKind.Class),
                        Access = MapAccessModifier(propNode.Access),
                        IsStatic = propNode.IsStatic,
                        IsReadOnly = propNode.IsReadOnly,
                        IsWriteOnly = propNode.IsWriteOnly
                    };

                    // Generate getter/setter methods
                    member.Accept(this);

                    // Find the generated getter/setter functions
                    var getterName = $"{node.Name}.get_{propNode.Name}";
                    var setterName = $"{node.Name}.set_{propNode.Name}";
                    prop.Getter = _module.Functions.FirstOrDefault(f => f.Name == getterName);
                    prop.Setter = _module.Functions.FirstOrDefault(f => f.Name == setterName);

                    irClass.Properties.Add(prop);
                }
                else
                {
                    member.Accept(this);
                }
            }

            _currentClassName = null;
        }

        private AccessModifier MapAccessModifier(BasicLang.Compiler.AST.AccessModifier access)
        {
            return access switch
            {
                BasicLang.Compiler.AST.AccessModifier.Public => AccessModifier.Public,
                BasicLang.Compiler.AST.AccessModifier.Private => AccessModifier.Private,
                BasicLang.Compiler.AST.AccessModifier.Protected => AccessModifier.Protected,
                _ => AccessModifier.Private
            };
        }

        public void Visit(InterfaceNode node)
        {
            var irInterface = new IRInterface(node.Name)
            {
                Namespace = _currentNamespace
            };

            foreach (var method in node.Methods)
            {
                var irMethod = new IRInterfaceMethod
                {
                    Name = method.Name,
                    ReturnType = new TypeInfo(method.ReturnType?.Name ?? "Void", TypeKind.Primitive),
                    HasDefaultImplementation = !method.IsAbstract && method.Body != null
                };

                foreach (var param in method.Parameters)
                {
                    irMethod.Parameters.Add(new IRParameter
                    {
                        Name = param.Name,
                        TypeName = param.Type?.Name ?? "Object",
                        IsOptional = param.IsOptional,
                        IsParamArray = param.IsParamArray,
                        IsByRef = param.IsByRef,
                        DefaultValue = BuildExpressionValue(param.DefaultValue)
                    });
                }

                // Generate IR for default implementation if present
                if (!method.IsAbstract && method.Body != null)
                {
                    var implFunctionName = $"{node.Name}.{method.Name}_DefaultImpl";
                    var savedFunction = _currentFunction;
                    var savedBlock = _currentBlock;

                    _currentFunction = _module.CreateFunction(implFunctionName, new TypeInfo(method.ReturnType?.Name ?? "Void", TypeKind.Primitive));

                    // Add parameters
                    foreach (var param in method.Parameters)
                    {
                        var paramType = _semanticAnalyzer.GetNodeType(param);
                        var irParam = new IRVariable(param.Name, paramType) { IsParameter = true };
                        _currentFunction.Parameters.Add(irParam);
                        PushVariableVersion(param.Name, irParam);
                    }

                    // Create entry block and generate body
                    _currentBlock = _currentFunction.CreateBlock("entry");
                    method.Body.Accept(this);

                    // Ensure function ends with return
                    if (!_currentBlock.IsTerminated())
                    {
                        if (method.ReturnType == null || method.ReturnType.Name == "Void")
                            EmitInstruction(new IRReturn());
                        else
                            EmitInstruction(new IRReturn(CreateDefaultValue(new TypeInfo(method.ReturnType.Name, TypeKind.Primitive))));
                    }

                    // Clean up
                    foreach (var param in method.Parameters)
                    {
                        PopVariableVersion(param.Name);
                    }

                    irMethod.DefaultImplementation = _currentFunction;

                    _currentFunction = savedFunction;
                    _currentBlock = savedBlock;
                }

                irInterface.Methods.Add(irMethod);
            }

            foreach (var prop in node.Properties)
            {
                irInterface.Properties.Add(new IRInterfaceProperty
                {
                    Name = prop.Name,
                    Type = new TypeInfo(prop.PropertyType?.Name ?? "Object", TypeKind.Class),
                    HasGetter = prop.Getter != null,
                    HasSetter = prop.Setter != null
                });
            }

            _module.Interfaces[node.Name] = irInterface;
        }

        public void Visit(EnumNode node)
        {
            var irEnum = new IREnum(node.Name)
            {
                Namespace = _currentNamespace,
                UnderlyingType = node.UnderlyingType != null
                    ? new TypeInfo(node.UnderlyingType.Name, TypeKind.Primitive)
                    : new TypeInfo("Int32", TypeKind.Primitive)
            };

            long nextValue = 0;
            foreach (var member in node.Members)
            {
                var irMember = new IREnumMember { Name = member.Name };

                // If member has explicit value, try to evaluate it
                if (member.Value != null)
                {
                    member.Value.Accept(this);
                    if (_expressionResult is IRConstant constant && constant.Value is long lval)
                    {
                        irMember.Value = lval;
                        nextValue = lval + 1;
                    }
                    else if (_expressionResult is IRConstant constant2 && constant2.Value is int ival)
                    {
                        irMember.Value = (long)ival;
                        nextValue = ival + 1;
                    }
                    else
                    {
                        irMember.Value = nextValue++;
                    }
                }
                else
                {
                    irMember.Value = nextValue++;
                }

                irEnum.Members.Add(irMember);
            }

            _module.Enums[node.Name] = irEnum;
        }

        public void Visit(EnumMemberNode node)
        {
            // Enum members are processed in EnumNode visitor
        }

        public void Visit(TypeNode node)
        {
            // User-defined types don't generate IR
        }

        public void Visit(StructureNode node)
        {
            // Structures don't generate IR directly
        }

        public void Visit(UnionNode node)
        {
            // Unions don't generate IR directly - handled by code generators
        }

        public void Visit(TemplateDeclarationNode node)
        {
            // Templates are expanded before IR generation
            if (node.Declaration != null)
            {
                node.Declaration.Accept(this);
            }
        }

        public void Visit(DelegateDeclarationNode node)
        {
            var irDelegate = new IRDelegate(node.Name)
            {
                Namespace = _currentNamespace,
                ReturnType = node.ReturnType != null
                    ? new TypeInfo(node.ReturnType.Name, TypeKind.Primitive)
                    : new TypeInfo("Void", TypeKind.Void)
            };

            foreach (var param in node.Parameters)
            {
                irDelegate.Parameters.Add(new IRParameter
                {
                    Name = param.Name,
                    TypeName = param.Type?.Name ?? "Object",
                    IsOptional = param.IsOptional,
                    IsParamArray = param.IsParamArray,
                    IsByRef = param.IsByRef,
                    DefaultValue = BuildExpressionValue(param.DefaultValue)
                });
            }

            _module.Delegates[node.Name] = irDelegate;
        }

        public void Visit(ExtensionMethodNode node)
        {
            // Extension methods are regular functions with extension marker
            if (node.Method != null)
            {
                node.Method.Accept(this);

                // Mark the function as an extension method
                var irFunc = _module.Functions.FirstOrDefault(f => f.Name == node.Method.Name);
                if (irFunc != null)
                {
                    irFunc.IsExtension = true;
                    irFunc.ExtendedType = node.ExtendedType;
                }
            }
        }

        public void Visit(ExternDeclarationNode node)
        {
            // Extern declarations are recorded in the module's extern table
            // They don't generate code themselves - they're used when the extern is called
            var externInfo = new IRExternDeclaration
            {
                Name = node.Name,
                IsFunction = node.IsFunction,
                ReturnType = new TypeInfo(node.ReturnType?.Name ?? "Void", node.ReturnType?.Name == "Void" || node.ReturnType == null ? TypeKind.Void : TypeKind.Primitive),
                PlatformImplementations = new Dictionary<string, string>(node.PlatformImplementations)
            };

            // Add parameters
            foreach (var param in node.Parameters)
            {
                externInfo.Parameters.Add(new IRParameter
                {
                    Name = param.Name,
                    TypeName = param.Type?.Name ?? "Object",
                    IsOptional = param.IsOptional,
                    IsParamArray = param.IsParamArray,
                    IsByRef = param.IsByRef,
                    DefaultValue = BuildExpressionValue(param.DefaultValue)
                });
            }

            _module.ExternDeclarations.Add(node.Name, externInfo);
        }

        public void Visit(ConstructorNode node)
        {
            // Generate constructor as a special method
            var constructorName = _currentClassName != null ? $"{_currentClassName}__ctor" : "Constructor";
            var returnType = new TypeInfo("Void", TypeKind.Void);

            _currentFunction = _module.CreateFunction(constructorName, returnType);
            _currentBlock = _currentFunction.CreateBlock("entry");

            // Add parameters
            foreach (var param in node.Parameters)
            {
                var paramType = _semanticAnalyzer.GetNodeType(param);
                var irParam = new IRVariable(param.Name, paramType) { IsParameter = true };
                _currentFunction.Parameters.Add(irParam);
                PushVariableVersion(param.Name, irParam);
            }

            // Process base constructor arguments and store them for the IRConstructor
            _pendingBaseConstructorArgs = new List<IRValue>();
            if (node.BaseConstructorArgs.Count > 0)
            {
                foreach (var arg in node.BaseConstructorArgs)
                {
                    arg.Accept(this);
                    if (_expressionResult != null)
                    {
                        _pendingBaseConstructorArgs.Add(_expressionResult);
                    }
                }
            }

            // Generate body
            if (node.Body != null)
            {
                node.Body.Accept(this);
            }

            // Add return if not terminated
            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRReturn());
            }

            _currentFunction = null;
            _currentBlock = null;
        }

        public void Visit(PropertyNode node)
        {
            var propertyType = node.PropertyType != null
                ? _semanticAnalyzer.GetNodeType(node) ?? new TypeInfo("Object", TypeKind.Class)
                : new TypeInfo("Object", TypeKind.Class);

            // Generate getter method
            if (node.Getter != null)
            {
                var getterName = _currentClassName != null
                    ? $"{_currentClassName}.get_{node.Name}"
                    : $"get_{node.Name}";

                _currentFunction = _module.CreateFunction(getterName, propertyType);
                _currentBlock = _currentFunction.CreateBlock("entry");

                node.Getter.Accept(this);

                if (!_currentBlock.IsTerminated())
                {
                    EmitInstruction(new IRReturn());
                }
            }

            // Generate setter method
            if (node.Setter != null)
            {
                var setterName = _currentClassName != null
                    ? $"{_currentClassName}.set_{node.Name}"
                    : $"set_{node.Name}";

                var voidType = new TypeInfo("Void", TypeKind.Void);
                _currentFunction = _module.CreateFunction(setterName, voidType);
                _currentBlock = _currentFunction.CreateBlock("entry");

                // Add value parameter
                var valueParam = new IRVariable("value", propertyType) { IsParameter = true };
                _currentFunction.Parameters.Add(valueParam);
                PushVariableVersion("value", valueParam);

                node.Setter.Accept(this);

                if (!_currentBlock.IsTerminated())
                {
                    EmitInstruction(new IRReturn());
                }
            }

            _currentFunction = null;
            _currentBlock = null;
        }

        public void Visit(MyBaseExpressionNode node)
        {
            // MyBase represents the base class instance
            // For now, treat it as a special "this" reference for base class access
            var baseType = _semanticAnalyzer.GetNodeType(node);
            _expressionResult = new IRVariable("__base", baseType);
        }

        public void Visit(LambdaExpressionNode node)
        {
            // Generate a unique name for the lambda function
            var lambdaName = $"__lambda_{_module.Functions.Count}";

            // Determine return type from semantic analysis
            var lambdaType = _semanticAnalyzer.GetNodeType(node);
            var returnType = node.IsFunction
                ? (_semanticAnalyzer.GetNodeType(node.Body) ?? new TypeInfo("Object", TypeKind.Class))
                : new TypeInfo("Void", TypeKind.Primitive);

            // If explicit return type specified, use it
            if (node.ReturnType != null)
            {
                returnType = new TypeInfo(node.ReturnType.Name, TypeKind.Class);
            }

            // Create the lambda function
            var lambdaFunc = new IRFunction(lambdaName, returnType);
            // lambdaFunc.IsLambda = true; // Property not yet implemented

            // Detect captured variables (variables from outer scopes)
            var capturedVars = new List<(string name, TypeInfo type)>();

            // Add parameters
            foreach (var param in node.Parameters)
            {
                var paramSymbol = _semanticAnalyzer.GetNodeSymbol(param);
                var paramType = paramSymbol?.Type ?? new TypeInfo("Object", TypeKind.Class);
                var paramVar = new IRVariable(param.Name, paramType)
                {
                    IsParameter = true
                };
                lambdaFunc.Parameters.Add(paramVar);
            }

            // Save current context
            var savedFunction = _currentFunction;
            var savedBlock = _currentBlock;
            var savedLocals = new Dictionary<string, IRAlloca>(_locals);

            // Set up lambda function context
            _currentFunction = lambdaFunc;
            _currentBlock = lambdaFunc.EntryBlock;
            _locals.Clear();

            // Allocate parameters as locals
            foreach (var param in node.Parameters)
            {
                var paramSymbol = _semanticAnalyzer.GetNodeSymbol(param);
                var paramType = paramSymbol?.Type ?? new TypeInfo("Object", TypeKind.Class);
                var alloca = new IRAlloca(param.Name, paramType);
                _locals[param.Name] = alloca;
                EmitInstruction(alloca);
            }

            // Generate body
            if (node.Body != null)
            {
                node.Body.Accept(this);
                // Return the expression result
                EmitInstruction(new IRReturn(_expressionResult));
            }
            else if (node.StatementBody != null)
            {
                node.StatementBody.Accept(this);
                // Ensure we have a return for void lambdas
                if (!_currentBlock.IsTerminated())
                {
                    EmitInstruction(new IRReturn(null));
                }
            }

            // Detect captured variables by checking which outer scope variables were accessed
            // This is a simplified approach - in a full implementation, we'd track this during body generation
            foreach (var kvp in savedLocals)
            {
                if (!_locals.ContainsKey(kvp.Key))
                {
                    // This variable from outer scope was potentially captured
                    // We'll let the C# backend handle this via closure conversion
                    capturedVars.Add((kvp.Key, kvp.Value.Type));
                }
            }

            // Store captured variables in the function metadata
            // lambdaFunc.CapturedVariables = capturedVars; // Property not yet implemented

            // Add lambda function to module
            _module.Functions.Add(lambdaFunc);

            // Restore context
            _currentFunction = savedFunction;
            _currentBlock = savedBlock;
            _locals.Clear();
            foreach (var kvp in savedLocals)
            {
                _locals[kvp.Key] = kvp.Value;
            }

            // The result is a reference to the lambda function (delegate)
            _expressionResult = new IRVariable(lambdaName, lambdaType ?? new TypeInfo("Delegate", TypeKind.Delegate));
        }

        public void Visit(CollectionInitializerNode node)
        {
            // Get the element type from semantic analysis
            var arrayType = _semanticAnalyzer.GetNodeType(node) as TypeInfo;
            var elementType = arrayType?.ElementType ?? new TypeInfo("Object", TypeKind.Class);

            // Create a list to hold the element values
            var elements = new List<IRValue>();

            foreach (var element in node.Elements)
            {
                element.Accept(this);
                elements.Add(_expressionResult);
            }

            // Create an array allocation IR
            var tempName = _currentFunction.GetNextTempName();
            var arrayAlloc = new IRArrayAlloc(tempName, elementType, elements.Count);
            EmitInstruction(arrayAlloc);

            // Store each element
            for (int i = 0; i < elements.Count; i++)
            {
                var indexConst = new IRConstant(i, new TypeInfo("Integer", TypeKind.Primitive));
                var store = new IRArrayStore(arrayAlloc, indexConst, elements[i]);
                EmitInstruction(store);
            }

            _expressionResult = arrayAlloc;
        }

        public void Visit(TupleLiteralNode node)
        {
            // Get the tuple type from semantic analysis
            var tupleType = _semanticAnalyzer.GetNodeType(node) as TypeInfo;

            // Evaluate all tuple elements
            var elementValues = new List<IRValue>();
            foreach (var element in node.Elements)
            {
                element.Accept(this);
                elementValues.Add(_expressionResult);
            }

            // Create a tuple IR node (represented as a call to tuple constructor)
            var tempName = _currentFunction.GetNextTempName();
            var tupleCall = new IRCall(
                tempName,
                "ValueTuple.Create",
                tupleType ?? new TypeInfo("Object", TypeKind.Class)
            );
            foreach (var arg in elementValues)
            {
                tupleCall.Arguments.Add(arg);
            }
            EmitInstruction(tupleCall);
            _expressionResult = tupleCall;
        }

        public void Visit(OperatorDeclarationNode node)
        {
            // Generate operator as a static method with special naming
            var opMethodName = $"op_{GetOperatorMethodName(node.OperatorSymbol)}";
            var returnType = new TypeInfo(node.ReturnType?.Name ?? "Object", TypeKind.Class);

            // Create the function with class-qualified name for the module
            var funcName = _currentClassName != null
                ? $"{_currentClassName}.{opMethodName}"
                : opMethodName;
            var opFunc = _module.CreateFunction(funcName, returnType);
            // Operators are always static (handled at code generation)

            // Add parameters
            foreach (var param in node.Parameters)
            {
                var paramType = new TypeInfo(param.Type?.Name ?? "Object", TypeKind.Class);
                var paramVar = new IRVariable(param.Name, paramType) { IsParameter = true };
                opFunc.Parameters.Add(paramVar);
                PushVariableVersion(param.Name, paramVar);
            }

            // Save context and switch to operator function
            var savedFunction = _currentFunction;
            var savedBlock = _currentBlock;
            var savedLocals = new Dictionary<string, IRAlloca>(_locals);

            _currentFunction = opFunc;
            _currentBlock = opFunc.CreateBlock("entry");
            _locals.Clear();

            // Generate body
            if (node.Body != null)
            {
                node.Body.Accept(this);
            }

            // Ensure return
            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRReturn(null));
            }

            // Clean up parameter versions
            foreach (var param in node.Parameters)
            {
                PopVariableVersion(param.Name);
            }

            // If inside a class, also add as an IRMethod
            if (_currentClassName != null && _module.Classes.TryGetValue(_currentClassName, out var irClass))
            {
                var irMethod = new IRMethod
                {
                    Name = opMethodName,
                    ReturnType = returnType,
                    Access = MapAccessModifier(node.Access),
                    IsStatic = true,  // Operators are always static
                    IsVirtual = false,
                    IsOverride = false,
                    Implementation = opFunc
                };
                irClass.Methods.Add(irMethod);
            }

            // Restore context
            _currentFunction = savedFunction;
            _currentBlock = savedBlock;
            _locals.Clear();
            foreach (var kvp in savedLocals)
            {
                _locals[kvp.Key] = kvp.Value;
            }
        }

        private string GetOperatorMethodName(string operatorSymbol)
        {
            return operatorSymbol switch
            {
                "+" => "Addition",
                "-" => "Subtraction",
                "*" => "Multiply",
                "/" => "Division",
                "\\" => "IntegerDivision",
                "Mod" => "Modulus",
                "^" => "Exponent",
                "=" => "Equality",
                "<>" => "Inequality",
                "<" => "LessThan",
                ">" => "GreaterThan",
                "<=" => "LessThanOrEqual",
                ">=" => "GreaterThanOrEqual",
                "&" => "Concatenate",
                "And" => "BitwiseAnd",
                "Or" => "BitwiseOr",
                "Xor" => "ExclusiveOr",
                "Not" => "OnesComplement",
                "IsTrue" => "True",
                "IsFalse" => "False",
                "CType" => "Implicit",
                _ => operatorSymbol.Replace(" ", "")
            };
        }

        public void Visit(EventDeclarationNode node)
        {
            // Add event to the current class
            if (_currentClassName != null && _module.Classes.TryGetValue(_currentClassName, out var irClass))
            {
                var irEvent = new IREvent
                {
                    Name = node.Name,
                    Access = MapAccessModifier(node.Access),
                    DelegateType = node.EventType?.Name ?? "EventHandler",
                    IsStatic = false
                };
                irClass.Events.Add(irEvent);
            }
        }

        public void Visit(RaiseEventStatementNode node)
        {
            // Evaluate arguments
            var args = new List<IRValue>();
            foreach (var arg in node.Arguments)
            {
                arg.Accept(this);
                args.Add(_expressionResult);
            }

            // Generate call to event invocation
            var eventCall = new IRCall(
                _currentFunction.GetNextTempName(),
                $"raise_{node.EventName}",
                new TypeInfo("Void", TypeKind.Void)
            );
            foreach (var arg in args)
            {
                eventCall.Arguments.Add(arg);
            }
            EmitInstruction(eventCall);
        }

        public void Visit(AddHandlerStatementNode node)
        {
            // Evaluate event and handler expressions
            node.EventExpression?.Accept(this);
            var eventExpr = _expressionResult;

            node.HandlerExpression?.Accept(this);
            var handlerExpr = _expressionResult;

            // Generate delegate combination call
            var addCall = new IRCall(
                _currentFunction.GetNextTempName(),
                "Delegate.Combine",
                new TypeInfo("Delegate", TypeKind.Delegate)
            );
            if (eventExpr != null) addCall.Arguments.Add(eventExpr);
            if (handlerExpr != null) addCall.Arguments.Add(handlerExpr);
            EmitInstruction(addCall);
        }

        public void Visit(RemoveHandlerStatementNode node)
        {
            // Evaluate event and handler expressions
            node.EventExpression?.Accept(this);
            var eventExpr = _expressionResult;

            node.HandlerExpression?.Accept(this);
            var handlerExpr = _expressionResult;

            // Generate delegate removal call
            var removeCall = new IRCall(
                _currentFunction.GetNextTempName(),
                "Delegate.Remove",
                new TypeInfo("Delegate", TypeKind.Delegate)
            );
            if (eventExpr != null) removeCall.Arguments.Add(eventExpr);
            if (handlerExpr != null) removeCall.Arguments.Add(handlerExpr);
            EmitInstruction(removeCall);
        }

        public void Visit(TypePatternNode node)
        {
            // Type patterns are handled in the Select Case code generation
            // The pattern generates a type check: If TypeOf expr Is Type Then
            // For now, just ensure the When guard is evaluated if present
            if (node.WhenGuard != null)
            {
                node.WhenGuard.Accept(this);
            }
        }

        public void Visit(ConstantPatternNode node)
        {
            // Constant patterns generate equality checks
            // Evaluate the constant value
            node.Value?.Accept(this);

            if (node.WhenGuard != null)
            {
                node.WhenGuard.Accept(this);
            }
        }

        public void Visit(RangePatternNode node)
        {
            // Range patterns generate: lower <= expr AndAlso expr <= upper
            node.LowerBound?.Accept(this);
            var lowerValue = _expressionResult;

            node.UpperBound?.Accept(this);
            var upperValue = _expressionResult;

            if (node.WhenGuard != null)
            {
                node.WhenGuard.Accept(this);
            }
        }

        public void Visit(ComparisonPatternNode node)
        {
            // Comparison patterns generate: expr op value
            node.Value?.Accept(this);

            if (node.WhenGuard != null)
            {
                node.WhenGuard.Accept(this);
            }
        }

        public void Visit(AwaitExpressionNode node)
        {
            IRValue taskExpr;

            // Special handling for CallExpressionNode - don't emit it separately
            // Instead, create the IRCall and embed it in the IRAwait
            if (node.Expression is CallExpressionNode callNode)
            {
                string functionName = "";
                if (callNode.Callee is IdentifierExpressionNode idExpr)
                {
                    functionName = idExpr.Name;
                }
                else if (callNode.Callee is MemberAccessExpressionNode memberExpr)
                {
                    functionName = $"{memberExpr.Object}.{memberExpr.MemberName}";
                }

                var returnType = _semanticAnalyzer.GetNodeType(callNode);
                var tempName = _currentFunction.GetNextTempName();
                var call = new IRCall(tempName, functionName, returnType);

                // Evaluate arguments
                foreach (var arg in callNode.Arguments)
                {
                    arg.Accept(this);
                    call.Arguments.Add(_expressionResult);
                }

                // Don't emit the call - it will be part of the await
                taskExpr = call;
            }
            else
            {
                // For non-call expressions, evaluate normally
                node.Expression?.Accept(this);
                taskExpr = _expressionResult;
            }

            // Generate IRAwait instruction
            var resultName = _currentFunction.GetNextTempName();
            var resultType = taskExpr?.Type ?? new TypeInfo("Object", TypeKind.Class);
            var awaitInst = new IRAwait(resultName, taskExpr, resultType);
            EmitInstruction(awaitInst);

            _expressionResult = awaitInst;
        }

        public void Visit(YieldStatementNode node)
        {
            if (node.IsBreak)
            {
                // Yield Break - generate IRYield with IsBreak = true
                EmitInstruction(new IRYield(null, isBreak: true));
            }
            else
            {
                // Yield Return - yield a value
                node.Value?.Accept(this);
                var yieldValue = _expressionResult;

                // Generate IRYield instruction
                EmitInstruction(new IRYield(yieldValue, isBreak: false));
            }
        }

        public void Visit(LinqQueryExpressionNode node)
        {
            // LINQ queries are converted to method chain calls
            // Store the query as a special IR node for code generation
            IRValue result = null;

            foreach (var clause in node.Clauses)
            {
                switch (clause)
                {
                    case FromClause from:
                        from.Collection?.Accept(this);
                        result = _expressionResult;
                        break;

                    case WhereClause where:
                        where.Condition?.Accept(this);
                        var whereCondition = _expressionResult;
                        var whereCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            "Where",
                            new TypeInfo("IEnumerable", TypeKind.Interface));
                        if (result != null) whereCall.Arguments.Add(result);
                        whereCall.Arguments.Add(whereCondition);
                        EmitInstruction(whereCall);
                        result = whereCall;
                        break;

                    case SelectClause select:
                        select.Selector?.Accept(this);
                        var selectExpr = _expressionResult;
                        var selectCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            "Select",
                            new TypeInfo("IEnumerable", TypeKind.Interface));
                        if (result != null) selectCall.Arguments.Add(result);
                        selectCall.Arguments.Add(selectExpr);
                        EmitInstruction(selectCall);
                        result = selectCall;
                        break;

                    case OrderByClause orderBy:
                        orderBy.KeySelector?.Accept(this);
                        var orderKey = _expressionResult;
                        var orderMethod = orderBy.Descending ? "OrderByDescending" : "OrderBy";
                        var orderCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            orderMethod,
                            new TypeInfo("IOrderedEnumerable", TypeKind.Interface));
                        if (result != null) orderCall.Arguments.Add(result);
                        orderCall.Arguments.Add(orderKey);
                        EmitInstruction(orderCall);
                        result = orderCall;
                        break;

                    case GroupByClause groupBy:
                        groupBy.KeySelector?.Accept(this);
                        var groupKey = _expressionResult;

                        var groupCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            "GroupBy",
                            new TypeInfo("IEnumerable", TypeKind.Interface));
                        if (result != null) groupCall.Arguments.Add(result);
                        groupCall.Arguments.Add(groupKey);

                        // If there's an element selector
                        if (groupBy.ElementSelector != null)
                        {
                            groupBy.ElementSelector.Accept(this);
                            groupCall.Arguments.Add(_expressionResult);
                        }

                        EmitInstruction(groupCall);
                        result = groupCall;
                        break;

                    case JoinClause join:
                        join.Collection?.Accept(this);
                        var innerCollection = _expressionResult;

                        join.OuterKeySelector?.Accept(this);
                        var outerKey = _expressionResult;

                        join.InnerKeySelector?.Accept(this);
                        var innerKey = _expressionResult;

                        var joinMethod = !string.IsNullOrEmpty(join.IntoVariable) ? "GroupJoin" : "Join";
                        var joinCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            joinMethod,
                            new TypeInfo("IEnumerable", TypeKind.Interface));
                        if (result != null) joinCall.Arguments.Add(result);
                        joinCall.Arguments.Add(innerCollection);
                        joinCall.Arguments.Add(outerKey);
                        joinCall.Arguments.Add(innerKey);

                        EmitInstruction(joinCall);
                        result = joinCall;
                        break;

                    case AggregateClause aggregate:
                        aggregate.Collection?.Accept(this);
                        var aggCollection = _expressionResult;

                        var aggCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            "Aggregate",
                            new TypeInfo("Object", TypeKind.Class));
                        if (result != null) aggCall.Arguments.Add(result);
                        aggCall.Arguments.Add(aggCollection);

                        if (aggregate.Selector != null)
                        {
                            aggregate.Selector.Accept(this);
                            aggCall.Arguments.Add(_expressionResult);
                        }

                        EmitInstruction(aggCall);
                        result = aggCall;
                        break;

                    case LetClause let:
                        // Let clauses create projection with additional property
                        // We'll represent this as a Select that creates an anonymous type
                        let.Value?.Accept(this);
                        var letValue = _expressionResult;

                        var letCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            "Select",
                            new TypeInfo("IEnumerable", TypeKind.Interface));
                        if (result != null) letCall.Arguments.Add(result);
                        letCall.Arguments.Add(letValue);

                        EmitInstruction(letCall);
                        result = letCall;
                        break;

                    case TakeClause take:
                        take.Count?.Accept(this);
                        var takeCount = _expressionResult;
                        var takeCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            "Take",
                            new TypeInfo("IEnumerable", TypeKind.Interface));
                        if (result != null) takeCall.Arguments.Add(result);
                        takeCall.Arguments.Add(takeCount);
                        EmitInstruction(takeCall);
                        result = takeCall;
                        break;

                    case SkipClause skip:
                        skip.Count?.Accept(this);
                        var skipCount = _expressionResult;
                        var skipCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            "Skip",
                            new TypeInfo("IEnumerable", TypeKind.Interface));
                        if (result != null) skipCall.Arguments.Add(result);
                        skipCall.Arguments.Add(skipCount);
                        EmitInstruction(skipCall);
                        result = skipCall;
                        break;

                    case DistinctClause:
                        var distinctCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            "Distinct",
                            new TypeInfo("IEnumerable", TypeKind.Interface));
                        if (result != null) distinctCall.Arguments.Add(result);
                        EmitInstruction(distinctCall);
                        result = distinctCall;
                        break;
                }
            }

            _expressionResult = result;
        }

        public void Visit(InlineCodeNode node)
        {
            // Create an inline code instruction that the code generator will handle
            var inlineInstr = new IRInlineCode(node.Language, node.Code);
            _currentBlock.AddInstruction(inlineInstr);
        }

        // ====================================================================
        // Preprocessor Directives
        // ====================================================================
        // Note: Preprocessor directives are typically processed before IR generation.
        // These methods handle cases where preprocessor nodes reach the IR builder.

        public void Visit(PreprocessorDefineNode node)
        {
            // Preprocessor #Define is typically handled during preprocessing
            _currentBlock.AddInstruction(new IRComment($"#Define {node.Name}" + (node.Value != null ? $" = {node.Value}" : "")));
        }

        public void Visit(PreprocessorUndefineNode node)
        {
            _currentBlock.AddInstruction(new IRComment($"#Undefine {node.Name}"));
        }

        public void Visit(PreprocessorIfNode node)
        {
            // In a true preprocessor, this would conditionally include/exclude code
            // For now, emit all branches with comments
            _currentBlock.AddInstruction(new IRComment("#If block"));
            foreach (var stmt in node.ThenBody)
            {
                stmt.Accept(this);
            }

            foreach (var elseIf in node.ElseIfClauses)
            {
                _currentBlock.AddInstruction(new IRComment("#ElseIf block"));
                foreach (var stmt in elseIf.Body)
                {
                    stmt.Accept(this);
                }
            }

            if (node.ElseBody.Count > 0)
            {
                _currentBlock.AddInstruction(new IRComment("#Else block"));
                foreach (var stmt in node.ElseBody)
                {
                    stmt.Accept(this);
                }
            }
            _currentBlock.AddInstruction(new IRComment("#EndIf"));
        }

        public void Visit(PreprocessorIncludeNode node)
        {
            _currentBlock.AddInstruction(new IRComment($"#Include \"{node.FilePath}\""));
        }

        public void Visit(PreprocessorConstNode node)
        {
            _currentBlock.AddInstruction(new IRComment($"#Const {node.Name}"));
        }

        public void Visit(PreprocessorRegionNode node)
        {
            _currentBlock.AddInstruction(new IRComment($"#Region {node.Name}"));
            foreach (var stmt in node.Body)
            {
                stmt.Accept(this);
            }
            _currentBlock.AddInstruction(new IRComment("#End Region"));
        }

        public void Visit(DeclareNode node)
        {
            // Create an extern declaration in the IR module
            var externDecl = new IRExternDeclaration
            {
                Name = node.Name,
                IsFunction = node.IsFunction,
                LibraryName = node.LibraryName,
                AliasName = node.AliasName,
                CallingConvention = node.Convention.ToString()
            };

            // Add parameters
            foreach (var param in node.Parameters)
            {
                externDecl.Parameters.Add(new IRParameter
                {
                    Name = param.Name,
                    TypeName = param.Type?.Name ?? "Object",
                    Type = new TypeInfo(param.Type?.Name ?? "Object", TypeKind.Primitive),
                    IsOptional = param.IsOptional,
                    IsParamArray = param.IsParamArray,
                    IsByRef = param.IsByRef,
                    DefaultValue = BuildExpressionValue(param.DefaultValue)
                });
            }

            // Set return type
            if (node.IsFunction && node.ReturnType != null)
            {
                externDecl.ReturnType = new TypeInfo(node.ReturnType.Name, TypeKind.Primitive);
            }
            else
            {
                externDecl.ReturnType = new TypeInfo("Void", TypeKind.Void);
            }

            _module.ExternDeclarations[node.Name] = externDecl;
        }

        // ====================================================================
        // Statements
        // ====================================================================

        public void Visit(BlockNode node)
        {
            foreach (var statement in node.Statements)
            {
                statement.Accept(this);

                // Stop if we hit a terminator
                if (_currentBlock.IsTerminated())
                    break;
            }
        }

        public void Visit(IfStatementNode node)
        {
            // Evaluate condition
            node.Condition.Accept(this);
            var condition = _expressionResult;

            // Create blocks
            var thenBlock = _currentFunction.CreateBlock("if.then");
            var elseBlock = node.ElseBlock != null || node.ElseIfClauses.Count > 0
                ? _currentFunction.CreateBlock("if.else")
                : null;
            var mergeBlock = _currentFunction.CreateBlock("if.end");

            // Emit conditional branch
            var branchTarget = elseBlock ?? mergeBlock;
            EmitInstruction(new IRConditionalBranch(condition, thenBlock, branchTarget));

            // Generate then block
            _currentBlock = thenBlock;
            node.ThenBlock.Accept(this);
            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRBranch(mergeBlock));
            }

            // Generate else/elseif blocks
            if (node.ElseIfClauses.Count > 0 || node.ElseBlock != null)
            {
                _currentBlock = elseBlock;

                // Handle elseif chain
                foreach (var (elseIfCond, elseIfBlock) in node.ElseIfClauses)
                {
                    elseIfCond.Accept(this);
                    var elseIfCondition = _expressionResult;

                    var elseIfThen = _currentFunction.CreateBlock("elseif.then");
                    var elseIfNext = _currentFunction.CreateBlock("elseif.else");

                    EmitInstruction(new IRConditionalBranch(elseIfCondition, elseIfThen, elseIfNext));

                    _currentBlock = elseIfThen;
                    elseIfBlock.Accept(this);
                    if (!_currentBlock.IsTerminated())
                    {
                        EmitInstruction(new IRBranch(mergeBlock));
                    }

                    _currentBlock = elseIfNext;
                }

                // Final else block
                if (node.ElseBlock != null)
                {
                    node.ElseBlock.Accept(this);
                }

                if (!_currentBlock.IsTerminated())
                {
                    EmitInstruction(new IRBranch(mergeBlock));
                }
            }

            // Continue with merge block
            _currentBlock = mergeBlock;
        }

        public void Visit(SelectStatementNode node)
        {
            // Evaluate switch expression
            node.Expression.Accept(this);
            var switchValue = _expressionResult;

            // Create blocks
            var defaultBlock = _currentFunction.CreateBlock("switch.default");
            var endBlock = _currentFunction.CreateBlock("switch.end");

            var switchInst = new IRSwitch(switchValue, defaultBlock);

            // Generate case blocks
            var caseBlocks = new List<BasicBlock>();
            int caseIndex = 0;
            foreach (var caseClause in node.Cases)
            {
                if (caseClause.IsElse)
                {
                    // Default case
                    continue;
                }

                var caseBlock = _currentFunction.CreateBlock($"switch_case_{caseIndex++}");
                caseBlocks.Add(caseBlock);

                // Add case values (simple constant matching)
                foreach (var caseValue in caseClause.Values)
                {
                    caseValue.Accept(this);
                    var value = _expressionResult;
                    switchInst.Cases.Add((value, caseBlock));
                }

                // Add pattern cases
                foreach (var pattern in caseClause.Patterns)
                {
                    var patternCase = ConvertPatternToIR(pattern, caseBlock);
                    if (patternCase != null)
                    {
                        switchInst.PatternCases.Add(patternCase);
                    }
                }
            }

            EmitInstruction(switchInst);

            // Generate case bodies
            for (int i = 0; i < node.Cases.Count; i++)
            {
                var caseClause = node.Cases[i];

                if (caseClause.IsElse)
                {
                    _currentBlock = defaultBlock;
                }
                else
                {
                    _currentBlock = caseBlocks[i];
                }

                caseClause.Body.Accept(this);

                if (!_currentBlock.IsTerminated())
                {
                    EmitInstruction(new IRBranch(endBlock));
                }
            }

            // Default block (if no else case was provided)
            _currentBlock = defaultBlock;
            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRBranch(endBlock));
            }

            _currentBlock = endBlock;
        }

        private IRPatternCase ConvertPatternToIR(PatternNode pattern, BasicBlock target)
        {
            switch (pattern)
            {
                case TypePatternNode typePattern:
                    var typeCase = new IRTypePatternCase(
                        typePattern.MatchType?.Name ?? "Object",
                        target
                    );
                    typeCase.BindingVariable = typePattern.VariableName;
                    return typeCase;

                case RangePatternNode rangePattern:
                    rangePattern.LowerBound.Accept(this);
                    var lower = _expressionResult;
                    rangePattern.UpperBound.Accept(this);
                    var upper = _expressionResult;
                    return new IRRangePatternCase(lower, upper, target);

                case ComparisonPatternNode compPattern:
                    compPattern.Value.Accept(this);
                    var compValue = _expressionResult;
                    return new IRComparisonPatternCase(compPattern.Operator, compValue, target);

                case ConstantPatternNode constPattern:
                    constPattern.Value.Accept(this);
                    var constValue = _expressionResult;
                    return new IRConstantPatternCase(constValue, target);

                default:
                    return null;
            }
        }

        public void Visit(CaseClauseNode node)
        {
            // Handled in SelectStatementNode
        }

        public void Visit(ForLoopNode node)
        {
            // Create loop blocks
            var condBlock = _currentFunction.CreateBlock("for.cond");
            var bodyBlock = _currentFunction.CreateBlock("for.body");
            var incBlock = _currentFunction.CreateBlock("for.inc");
            var endBlock = _currentFunction.CreateBlock("for.end");

            // Initialize loop variable
            node.Start.Accept(this);
            var startValue = _expressionResult;

            var loopVar = GetOrCreateVariable(node.Variable, startValue.Type);
            EmitInstruction(new IRAssignment(loopVar, startValue));

            // Jump to condition
            EmitInstruction(new IRBranch(condBlock));

            // Condition block
            _currentBlock = condBlock;
            node.End.Accept(this);
            var endValue = _expressionResult;

            var tempName = _currentFunction.GetNextTempName();
            var cond = new IRCompare(tempName, CompareKind.Le, loopVar, endValue,
                new TypeInfo("Boolean", TypeKind.Primitive));
            EmitInstruction(cond);

            EmitInstruction(new IRConditionalBranch(cond, bodyBlock, endBlock));

            // Push loop context
            _loopStack.Push(new LoopContext(condBlock, endBlock));

            // Body block
            _currentBlock = bodyBlock;
            node.Body.Accept(this);

            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRBranch(incBlock));
            }

            // Increment block
            _currentBlock = incBlock;
            IRValue stepValue;
            if (node.Step != null)
            {
                node.Step.Accept(this);
                stepValue = _expressionResult;
            }
            else
            {
                stepValue = new IRConstant(1, new TypeInfo("Integer", TypeKind.Primitive));
            }

            var incTemp = _currentFunction.GetNextTempName();
            var inc = new IRBinaryOp(incTemp, BinaryOpKind.Add, loopVar, stepValue, loopVar.Type);
            EmitInstruction(inc);

            var newLoopVar = CreateVariable(node.Variable, loopVar.Type, _nextVersion++);
            EmitInstruction(new IRAssignment(newLoopVar, inc));
            PushVariableVersion(node.Variable, newLoopVar);

            EmitInstruction(new IRBranch(condBlock));

            // Pop loop context
            _loopStack.Pop();
            PopVariableVersion(node.Variable);

            // Continue with end block
            _currentBlock = endBlock;
        }

        public void Visit(ForEachLoopNode node)
        {
            // Simplified for-each - assumes array iteration
            node.Collection.Accept(this);
            var collection = _expressionResult;

            // Create loop blocks
            var condBlock = _currentFunction.CreateBlock("foreach.cond");
            var bodyBlock = _currentFunction.CreateBlock("foreach.body");
            var incBlock = _currentFunction.CreateBlock("foreach.inc");
            var endBlock = _currentFunction.CreateBlock("foreach.end");

            // Initialize index variable
            var indexVar = CreateVariable("__index", new TypeInfo("Integer", TypeKind.Primitive), _nextVersion++);
            EmitInstruction(new IRAssignment(indexVar, new IRConstant(0, indexVar.Type)));

            // Get array length (simplified - would need runtime support)
            var lengthTemp = _currentFunction.GetNextTempName();
            var length = new IRVariable(lengthTemp, new TypeInfo("Integer", TypeKind.Primitive));
            EmitInstruction(new IRComment("Get array length"));

            EmitInstruction(new IRBranch(condBlock));

            // Condition block
            _currentBlock = condBlock;
            var condTemp = _currentFunction.GetNextTempName();
            var cond = new IRCompare(condTemp, CompareKind.Lt, indexVar, length,
                new TypeInfo("Boolean", TypeKind.Primitive));
            EmitInstruction(cond);
            EmitInstruction(new IRConditionalBranch(cond, bodyBlock, endBlock));

            // Body block
            _currentBlock = bodyBlock;

            // Get element at index
            var gepTemp = _currentFunction.GetNextTempName();
            var elemType = _semanticAnalyzer.GetNodeType(node) ?? new TypeInfo("Object", TypeKind.Class);
            var gep = new IRGetElementPtr(gepTemp, collection, elemType);
            gep.Indices.Add(indexVar);
            EmitInstruction(gep);

            var loadTemp = _currentFunction.GetNextTempName();
            var element = new IRLoad(loadTemp, gep, elemType);
            EmitInstruction(element);

            // Assign to loop variable
            var loopVar = GetOrCreateVariable(node.Variable, elemType);
            EmitInstruction(new IRAssignment(loopVar, element));

            _loopStack.Push(new LoopContext(incBlock, endBlock));
            node.Body.Accept(this);
            _loopStack.Pop();

            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRBranch(incBlock));
            }

            // Increment block
            _currentBlock = incBlock;
            var incTemp = _currentFunction.GetNextTempName();
            var inc = new IRBinaryOp(incTemp, BinaryOpKind.Add, indexVar,
                new IRConstant(1, indexVar.Type), indexVar.Type);
            EmitInstruction(inc);

            var newIndex = CreateVariable("__index", indexVar.Type, _nextVersion++);
            EmitInstruction(new IRAssignment(newIndex, inc));

            EmitInstruction(new IRBranch(condBlock));

            // Continue with end block
            _currentBlock = endBlock;
        }

        // Stack of With object variables for nested With blocks
        private Stack<IRVariable> _withObjectStack = new Stack<IRVariable>();

        public void Visit(WithStatementNode node)
        {
            // Evaluate the With object expression
            node.Object.Accept(this);
            var withObject = _expressionResult;

            // Store the object in a temporary variable for use in the body
            var objType = _semanticAnalyzer.GetNodeType(node.Object) ?? new TypeInfo("Object", TypeKind.Class);
            var withVar = CreateVariable("__with", objType, _nextVersion++);
            EmitInstruction(new IRAssignment(withVar, withObject));

            // Push the With variable for implicit member access
            _withObjectStack.Push(withVar);

            EmitInstruction(new IRComment("With block"));

            // Process the body
            node.Body.Accept(this);

            _withObjectStack.Pop();
            EmitInstruction(new IRComment("End With"));
        }

        public void Visit(ImplicitWithMemberNode node)
        {
            if (_withObjectStack.Count == 0)
            {
                // Error already reported by semantic analyzer
                _expressionResult = new IRConstant(null, new TypeInfo("Object", TypeKind.Class));
                return;
            }

            var withVar = _withObjectStack.Peek();
            var memberType = _semanticAnalyzer.GetNodeType(node) ?? new TypeInfo("Object", TypeKind.Class);

            // Create a field access for the implicit member
            var tempName = _currentFunction.GetNextTempName();
            var fieldAccess = new IRFieldAccess(tempName, withVar, node.MemberName, memberType);

            _expressionResult = fieldAccess;
        }

        public void Visit(WhileLoopNode node)
        {
            var condBlock = _currentFunction.CreateBlock("while.cond");
            var bodyBlock = _currentFunction.CreateBlock("while.body");
            var endBlock = _currentFunction.CreateBlock("while.end");

            EmitInstruction(new IRBranch(condBlock));

            // Condition block
            _currentBlock = condBlock;
            node.Condition.Accept(this);
            var condition = _expressionResult;
            EmitInstruction(new IRConditionalBranch(condition, bodyBlock, endBlock));

            // Body block
            _currentBlock = bodyBlock;
            _loopStack.Push(new LoopContext(condBlock, endBlock));
            node.Body.Accept(this);
            _loopStack.Pop();

            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRBranch(condBlock));
            }

            // Continue with end block
            _currentBlock = endBlock;
        }

        public void Visit(DoLoopNode node)
        {
            var condBlock = _currentFunction.CreateBlock("do.cond");
            var bodyBlock = _currentFunction.CreateBlock("do.body");
            var endBlock = _currentFunction.CreateBlock("do.end");

            if (node.IsConditionAtStart && node.Condition != null)
            {
                // Do While/Until ... Loop - condition at start (like a while loop)
                EmitInstruction(new IRBranch(condBlock));

                // Condition block
                _currentBlock = condBlock;
                node.Condition.Accept(this);
                var condition = _expressionResult;

                // For Until, swap true/false branches
                if (node.IsWhile)
                    EmitInstruction(new IRConditionalBranch(condition, bodyBlock, endBlock));
                else
                    EmitInstruction(new IRConditionalBranch(condition, endBlock, bodyBlock));

                // Body block
                _currentBlock = bodyBlock;
                _loopStack.Push(new LoopContext(condBlock, endBlock));
                node.Body.Accept(this);
                _loopStack.Pop();

                if (!_currentBlock.IsTerminated())
                {
                    EmitInstruction(new IRBranch(condBlock));
                }
            }
            else
            {
                // Do ... Loop While/Until - condition at end (or infinite loop)
                EmitInstruction(new IRBranch(bodyBlock));

                // Body block
                _currentBlock = bodyBlock;
                _loopStack.Push(new LoopContext(condBlock, endBlock));
                node.Body.Accept(this);
                _loopStack.Pop();

                if (!_currentBlock.IsTerminated())
                {
                    EmitInstruction(new IRBranch(condBlock));
                }

                // Condition block
                _currentBlock = condBlock;
                if (node.Condition != null)
                {
                    node.Condition.Accept(this);
                    var condition = _expressionResult;

                    // For Until, swap true/false branches
                    if (node.IsWhile)
                        EmitInstruction(new IRConditionalBranch(condition, bodyBlock, endBlock));
                    else
                        EmitInstruction(new IRConditionalBranch(condition, endBlock, bodyBlock));
                }
                else
                {
                    // Infinite loop
                    EmitInstruction(new IRBranch(bodyBlock));
                }
            }

            // Continue with end block
            _currentBlock = endBlock;
        }

        public void Visit(TryStatementNode node)
        {
            // Create all blocks first
            var tryBlock = _currentFunction.CreateBlock("try.body");
            var endBlock = _currentFunction.CreateBlock("try.end");

            // Create catch blocks and build IRCatchClause list
            var catchClauses = new List<IRCatchClause>();
            var catchBlockList = new List<(CatchClauseNode clause, BasicBlock block)>();
            foreach (var catchClause in node.CatchClauses)
            {
                var catchBlock = _currentFunction.CreateBlock("catch.body");
                var exceptionType = catchClause.ExceptionType != null
                    ? new TypeInfo(catchClause.ExceptionType.Name, TypeKind.Class)
                    : new TypeInfo("Exception", TypeKind.Class);
                catchClauses.Add(new IRCatchClause(exceptionType, catchClause.ExceptionVariable, catchBlock));
                catchBlockList.Add((catchClause, catchBlock));
            }

            // Create finally block if present
            BasicBlock finallyBlock = null;
            if (node.FinallyBlock != null)
            {
                finallyBlock = _currentFunction.CreateBlock("finally.body");
            }

            // Emit the try-catch instruction in the current block
            EmitInstruction(new IRTryCatch(tryBlock, catchClauses, finallyBlock, endBlock));

            // Generate try block body
            _currentBlock = tryBlock;
            node.TryBlock.Accept(this);
            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRBranch(endBlock));
            }

            // Generate catch block bodies
            foreach (var (catchClause, catchBlock) in catchBlockList)
            {
                _currentBlock = catchBlock;

                // Declare the exception variable in scope if present
                var exceptionType = catchClause.ExceptionType != null
                    ? new TypeInfo(catchClause.ExceptionType.Name, TypeKind.Class)
                    : new TypeInfo("Exception", TypeKind.Class);
                if (!string.IsNullOrEmpty(catchClause.ExceptionVariable))
                {
                    // Create a local variable for the exception
                    var exVar = new IRVariable(catchClause.ExceptionVariable, exceptionType);
                    // Push onto variable versions stack so it's accessible in the catch block
                    if (!_variableVersions.ContainsKey(catchClause.ExceptionVariable))
                    {
                        _variableVersions[catchClause.ExceptionVariable] = new Stack<IRVariable>();
                    }
                    _variableVersions[catchClause.ExceptionVariable].Push(exVar);
                }

                catchClause.Body.Accept(this);

                // Pop the exception variable
                if (!string.IsNullOrEmpty(catchClause.ExceptionVariable) &&
                    _variableVersions.ContainsKey(catchClause.ExceptionVariable))
                {
                    _variableVersions[catchClause.ExceptionVariable].Pop();
                }

                if (!_currentBlock.IsTerminated())
                {
                    EmitInstruction(new IRBranch(endBlock));
                }
            }

            // Generate finally block body if present
            if (finallyBlock != null)
            {
                _currentBlock = finallyBlock;
                node.FinallyBlock.Accept(this);
                if (!_currentBlock.IsTerminated())
                {
                    EmitInstruction(new IRBranch(endBlock));
                }
            }

            _currentBlock = endBlock;
        }

        public void Visit(CatchClauseNode node)
        {
            // Handled in TryStatementNode
        }

        public void Visit(ThrowStatementNode node)
        {
            EmitInstruction(new IRComment("Throw exception"));
            if (node.Exception != null)
            {
                node.Exception.Accept(this);
                // In a real implementation, we would emit an IR instruction for throw
                // For now, we just emit a comment as the backends will handle this specially
            }
        }

        public void Visit(ReturnStatementNode node)
        {
            if (node.Value != null)
            {
                node.Value.Accept(this);
                EmitInstruction(new IRReturn(_expressionResult));
            }
            else
            {
                EmitInstruction(new IRReturn());
            }
        }

        public void Visit(ExitStatementNode node)
        {
            switch (node.Kind)
            {
                case ExitKind.For:
                case ExitKind.Do:
                case ExitKind.While:
                    // Jump to the break target of the current loop
                    if (_loopStack.Count == 0)
                    {
                        throw new Exception($"Exit {node.Kind} outside of loop");
                    }
                    var loopContext = _loopStack.Peek();
                    EmitInstruction(new IRBranch(loopContext.BreakTarget));
                    break;

                case ExitKind.Sub:
                case ExitKind.Function:
                    // Exit Sub/Function is like Return (without value for Sub)
                    EmitInstruction(new IRReturn());
                    break;
            }
        }

        public void Visit(AssignmentStatementNode node)
        {
            // Evaluate right-hand side
            node.Value.Accept(this);
            var value = _expressionResult;

            // Handle compound assignments
            if (node.Operator != "=")
            {
                // Load current value
                node.Target.Accept(this);
                var currentValue = _expressionResult;

                // Determine operation
                BinaryOpKind op = node.Operator switch
                {
                    "+=" or "=+" => BinaryOpKind.Add,
                    "-=" or "=-" => BinaryOpKind.Sub,
                    "*=" => BinaryOpKind.Mul,
                    "/=" => BinaryOpKind.Div,
                    _ => throw new Exception($"Unknown assignment operator: {node.Operator}")
                };

                var tempName = _currentFunction.GetNextTempName();
                var result = new IRBinaryOp(tempName, op, currentValue, value, currentValue.Type);
                EmitInstruction(result);
                value = result;
            }

            // Store to target
            if (node.Target is IdentifierExpressionNode idExpr)
            {
                var targetVar = GetOrCreateVariable(idExpr.Name, value.Type);

                // Optimization: If the value is a direct call or binary op result,
                // rename it to the target variable instead of creating a separate assignment
                if (value is IRCall call)
                {
                    // Rename the call's result to the target variable
                    call.Name = targetVar.Name;
                    // No need to emit IRAssignment - the call now directly assigns to target
                }
                else if (value is IRBinaryOp binOp)
                {
                    // Rename the binary op's result to the target variable
                    binOp.Name = targetVar.Name;
                    // No need to emit IRAssignment
                }
                else if (value is IRUnaryOp unaryOp)
                {
                    // Rename the unary op's result to the target variable
                    unaryOp.Name = targetVar.Name;
                    // No need to emit IRAssignment
                }
                else if (value is IRCompare compare)
                {
                    // Rename the compare result to the target variable
                    compare.Name = targetVar.Name;
                    // No need to emit IRAssignment
                }
                else
                {
                    // For constants, variables, or other values, emit an assignment
                    EmitInstruction(new IRAssignment(targetVar, value));
                }
            }
            else if (node.Target is MemberAccessExpressionNode memberExpr)
            {
                // Handle member assignment (both properties and fields use field store syntax in C#)
                memberExpr.Object.Accept(this);
                var obj = _expressionResult;

                var fieldStore = new IRFieldStore(obj, memberExpr.MemberName, value);
                EmitInstruction(fieldStore);
            }
            else if (node.Target is ArrayAccessExpressionNode arrayExpr)
            {
                // Handle array element assignment
                arrayExpr.Array.Accept(this);
                var array = _expressionResult;

                // Get element pointer
                var gepTemp = _currentFunction.GetNextTempName();
                var gep = new IRGetElementPtr(gepTemp, array, value.Type);
                foreach (var index in arrayExpr.Indices)
                {
                    index.Accept(this);
                    gep.Indices.Add(_expressionResult);
                }
                EmitInstruction(gep);

                // Store to pointer
                EmitInstruction(new IRStore(value, gep));
            }
        }

        public void Visit(ExpressionStatementNode node)
        {
            node.Expression.Accept(this);
        }

        // ====================================================================
        // Expressions
        // ====================================================================

        private IRValue _expressionResult;

        /// <summary>
        /// Build an expression and return the result. Used for default parameter values.
        /// </summary>
        private IRValue BuildExpressionValue(ExpressionNode expr)
        {
            if (expr == null) return null;

            // Handle literal expressions directly
            if (expr is LiteralExpressionNode literal)
            {
                var type = _semanticAnalyzer.GetNodeType(literal);
                return new IRConstant(literal.Value, type);
            }

            // For other expressions, visit and capture the result
            var savedResult = _expressionResult;
            expr.Accept(this);
            var result = _expressionResult;
            _expressionResult = savedResult;
            return result;
        }

        public void Visit(BinaryExpressionNode node)
        {
            node.Left.Accept(this);
            var left = _expressionResult;

            node.Right.Accept(this);
            var right = _expressionResult;

            var resultType = _semanticAnalyzer.GetNodeType(node);
            var tempName = _currentFunction.GetNextTempName();

            // Map operator
            IRValue result;

            if (IsComparisonOperator(node.Operator))
            {
                var cmpKind = MapComparisonOperator(node.Operator);
                result = new IRCompare(tempName, cmpKind, left, right, resultType);
            }
            else
            {
                var opKind = MapBinaryOperator(node.Operator);
                result = new IRBinaryOp(tempName, opKind, left, right, resultType);
            }

            EmitInstruction(result);
            _expressionResult = result;
        }

        public void Visit(UnaryExpressionNode node)
        {
            node.Operand.Accept(this);
            var operand = _expressionResult;

            var resultType = _semanticAnalyzer.GetNodeType(node);
            var tempName = _currentFunction.GetNextTempName();

            var opKind = MapUnaryOperator(node.Operator);
            var result = new IRUnaryOp(tempName, opKind, operand, resultType);

            EmitInstruction(result);
            _expressionResult = result;
        }

        public void Visit(LiteralExpressionNode node)
        {
            var type = _semanticAnalyzer.GetNodeType(node);
            _expressionResult = new IRConstant(node.Value, type);
        }

        public void Visit(InterpolatedStringNode node)
        {
            var stringType = new TypeInfo("String", TypeKind.Primitive);

            // Build the string by concatenating all parts
            IRValue result = null;

            foreach (var part in node.Parts)
            {
                IRValue partValue;

                if (part is string text)
                {
                    // Literal text part
                    partValue = new IRConstant(text, stringType);
                }
                else if (part is ExpressionNode expr)
                {
                    // Expression part - evaluate and convert to string
                    expr.Accept(this);
                    var exprValue = _expressionResult;

                    // If not already a string, convert to string
                    var exprType = _semanticAnalyzer.GetNodeType(expr);
                    if (exprType?.Name != "String")
                    {
                        var tempName = _currentFunction.GetNextTempName();
                        var toStringCall = new IRCall(tempName, "ToString", stringType);
                        toStringCall.Arguments.Add(exprValue);
                        EmitInstruction(toStringCall);
                        partValue = toStringCall;
                    }
                    else
                    {
                        partValue = exprValue;
                    }
                }
                else
                {
                    continue;
                }

                // Concatenate with previous result
                if (result == null)
                {
                    result = partValue;
                }
                else
                {
                    var tempName = _currentFunction.GetNextTempName();
                    var concat = new IRBinaryOp(tempName, BinaryOpKind.Concat, result, partValue, stringType);
                    EmitInstruction(concat);
                    result = concat;
                }
            }

            _expressionResult = result ?? new IRConstant("", stringType);
        }

        public void Visit(IdentifierExpressionNode node)
        {
            // Look up variable
            var variable = GetOrCreateVariable(node.Name, _semanticAnalyzer.GetNodeType(node));
            _expressionResult = variable;
        }

        public void Visit(MemberAccessExpressionNode node)
        {
            node.Object.Accept(this);
            var obj = _expressionResult;

            // Generate field access
            var memberType = _semanticAnalyzer.GetNodeType(node);
            var tempName = _currentFunction.GetNextTempName();

            var fieldAccess = new IRFieldAccess(tempName, obj, node.MemberName, memberType);
            EmitInstruction(fieldAccess);

            _expressionResult = fieldAccess;
        }

        public void Visit(CallExpressionNode node)
        {
            var returnType = _semanticAnalyzer.GetNodeType(node);
            var tempName = returnType != null && returnType.Name != "Void"
                ? _currentFunction.GetNextTempName()
                : null;

            // Check for different call types
            if (node.Callee is MemberAccessExpressionNode memberExpr)
            {
                // Check if this is a MyBase call
                if (memberExpr.Object is MyBaseExpressionNode)
                {
                    // Base class method call: MyBase.Method(args)
                    var baseCall = new IRBaseMethodCall(tempName, memberExpr.MemberName, returnType);

                    foreach (var arg in node.Arguments)
                    {
                        arg.Accept(this);
                        baseCall.Arguments.Add(_expressionResult);
                    }

                    EmitInstruction(baseCall);
                    _expressionResult = baseCall;
                    return;
                }

                // Check if this is an instance method call vs static method call
                // Instance: obj.Method() where obj is a variable
                // Static: ClassName.Method() where ClassName is a type
                memberExpr.Object.Accept(this);
                var obj = _expressionResult;

                // Determine if this is a static call (type reference) or instance call (variable)
                // Check if the object is a reference to a class type (static call) by:
                // 1. It's an IRVariable with the EXACT name of a class (case-sensitive)
                // 2. The identifier doesn't match a local variable in the current function
                // 3. It's a known .NET static type (Console, Math, File, etc.)
                bool isStaticCall = false;
                if (obj is IRVariable objVar)
                {
                    // Case-sensitive check: does the name exactly match a class name?
                    bool exactClassMatch = _module.Classes.Keys.Any(k => k == objVar.Name);
                    // Is this a local variable or parameter?
                    bool isLocalOrParam = _currentFunction?.Parameters.Any(p => p.Name == objVar.Name) == true ||
                                          _locals.ContainsKey(objVar.Name);

                    // Check if it's a .NET type (contains dot or is known .NET type name)
                    bool isNetType = objVar.Name.Contains('.') || IsKnownNetStaticType(objVar.Name);

                    // It's a static call if:
                    // - It matches a class name exactly AND is not a local/param, OR
                    // - It's a .NET type
                    isStaticCall = (exactClassMatch && !isLocalOrParam) || isNetType;
                }

                if (isStaticCall && obj is IRVariable staticVar)
                {
                    // Static method call: ClassName.Method()
                    var call = new IRCall(tempName, $"{staticVar.Name}.{memberExpr.MemberName}", returnType);
                    foreach (var arg in node.Arguments)
                    {
                        arg.Accept(this);
                        call.Arguments.Add(_expressionResult);
                    }
                    EmitInstruction(call);
                    _expressionResult = call;
                }
                else
                {
                    // Instance method call: obj.Method()
                    var methodCall = new IRInstanceMethodCall(tempName, obj, memberExpr.MemberName, returnType);

                    foreach (var arg in node.Arguments)
                    {
                        arg.Accept(this);
                        methodCall.Arguments.Add(_expressionResult);
                    }

                    EmitInstruction(methodCall);
                    _expressionResult = methodCall;
                }
            }
            else if (node.Callee is IdentifierExpressionNode idExpr)
            {
                // Regular function call
                var call = new IRCall(tempName, idExpr.Name, returnType);

                // Get function symbol to check for ByRef parameters
                var funcSymbol = _semanticAnalyzer.GetNodeSymbol(node.Callee);

                for (int i = 0; i < node.Arguments.Count; i++)
                {
                    node.Arguments[i].Accept(this);
                    call.Arguments.Add(_expressionResult);

                    // Check if this parameter is ByRef
                    bool isByRef = false;
                    if (funcSymbol?.Parameters != null && i < funcSymbol.Parameters.Count)
                    {
                        isByRef = funcSymbol.Parameters[i].IsByRef;
                    }
                    call.ByRefArguments.Add(isByRef);
                }

                EmitInstruction(call);
                _expressionResult = call;
            }
            else
            {
                // Fallback
                node.Callee.Accept(this);
                var callee = _expressionResult;
                var call = new IRCall(tempName, callee?.Name ?? "unknown", returnType);

                foreach (var arg in node.Arguments)
                {
                    arg.Accept(this);
                    call.Arguments.Add(_expressionResult);
                }

                EmitInstruction(call);
                _expressionResult = call;
            }
        }

        public void Visit(ArrayAccessExpressionNode node)
        {
            node.Array.Accept(this);
            var array = _expressionResult;

            var elementType = _semanticAnalyzer.GetNodeType(node);
            var gepTemp = _currentFunction.GetNextTempName();
            var gep = new IRGetElementPtr(gepTemp, array, elementType);

            foreach (var index in node.Indices)
            {
                index.Accept(this);
                gep.Indices.Add(_expressionResult);
            }

            EmitInstruction(gep);

            // Load from array element
            var loadTemp = _currentFunction.GetNextTempName();
            var load = new IRLoad(loadTemp, gep, elementType);
            EmitInstruction(load);

            _expressionResult = load;
        }

        public void Visit(NewExpressionNode node)
        {
            var type = _semanticAnalyzer.GetNodeType(node);
            var tempName = _currentFunction.GetNextTempName();
            var className = node.Type?.Name ?? "Object";

            // Create new object instruction
            var newObj = new IRNewObject(tempName, className, type);

            // Evaluate arguments
            foreach (var arg in node.Arguments)
            {
                arg.Accept(this);
                newObj.Arguments.Add(_expressionResult);
            }

            EmitInstruction(newObj);
            _expressionResult = newObj;
        }

        public void Visit(CastExpressionNode node)
        {
            node.Expression.Accept(this);
            var value = _expressionResult;

            var sourceType = _semanticAnalyzer.GetNodeType(node.Expression);
            var targetType = _semanticAnalyzer.GetNodeType(node);

            var tempName = _currentFunction.GetNextTempName();
            var castKind = DetermineCastKind(sourceType, targetType);

            var cast = new IRCast(tempName, value, sourceType, targetType, castKind);
            EmitInstruction(cast);

            _expressionResult = cast;
        }

        // ====================================================================
        // Helper Methods
        // ====================================================================

        private IRConstant CreateDefaultValue(TypeInfo type)
        {
            if (type.Name == "Integer" || type.Name == "Long")
                return new IRConstant(0, type);
            if (type.Name == "Single" || type.Name == "Double")
                return new IRConstant(0.0, type);
            if (type.Name == "Boolean")
                return new IRConstant(false, type);
            if (type.Name == "String")
                return new IRConstant("", type);

            return new IRConstant(null, type);
        }

        private bool IsComparisonOperator(string op)
        {
            return op == "<" || op == "<=" || op == ">" || op == ">=" ||
                   op == "=" || op == "<>" || op == "==" || op == "!=" || op == "IsEqual";
        }

        private CompareKind MapComparisonOperator(string op)
        {
            return op switch
            {
                "=" or "==" or "IsEqual" => CompareKind.Eq,
                "<>" or "!=" => CompareKind.Ne,
                "<" => CompareKind.Lt,
                "<=" => CompareKind.Le,
                ">" => CompareKind.Gt,
                ">=" => CompareKind.Ge,
                _ => throw new Exception($"Unknown comparison operator: {op}")
            };
        }

        private BinaryOpKind MapBinaryOperator(string op)
        {
            return op switch
            {
                "+" => BinaryOpKind.Add,
                "-" => BinaryOpKind.Sub,
                "*" => BinaryOpKind.Mul,
                "/" => BinaryOpKind.Div,
                "\\" => BinaryOpKind.IntDiv,
                "%" => BinaryOpKind.Mod,
                "&" => BinaryOpKind.Concat,
                "And" or "&&" => BinaryOpKind.And,
                "Or" or "||" => BinaryOpKind.Or,
                _ => throw new Exception($"Unknown binary operator: {op}")
            };
        }

        private UnaryOpKind MapUnaryOperator(string op)
        {
            return op switch
            {
                "-" => UnaryOpKind.Neg,
                "Not" or "!" => UnaryOpKind.Not,
                "++" => UnaryOpKind.Inc,
                "--" => UnaryOpKind.Dec,
                "AddressOf" => UnaryOpKind.AddressOf,
                _ => throw new Exception($"Unknown unary operator: {op}")
            };
        }

        private CastKind DetermineCastKind(TypeInfo source, TypeInfo target)
        {
            if (source.IsFloatingPoint() && target.IsIntegral())
                return CastKind.FPToSI;
            if (source.IsIntegral() && target.IsFloatingPoint())
                return CastKind.SIToFP;
            if (source.IsIntegral() && target.IsIntegral())
                return source.Name == "Long" ? CastKind.Trunc : CastKind.SExt;
            if (source.IsFloatingPoint() && target.IsFloatingPoint())
                return source.Name == "Double" ? CastKind.FPTrunc : CastKind.FPExt;

            return CastKind.Bitcast;
        }

        /// <summary>
        /// Known .NET static types/classes that should be treated as static method targets
        /// </summary>
        private static readonly HashSet<string> KnownNetStaticTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // System types
            "Console", "Math", "Environment", "Convert", "BitConverter",
            "String", "Char", "Int32", "Int64", "Double", "Single", "Boolean", "Byte",
            "Int16", "UInt16", "UInt32", "UInt64", "Decimal", "SByte",
            "Object", "DateTime", "TimeSpan", "Guid", "Random",
            "Activator", "Type", "Enum", "Array", "Buffer",
            "GC", "AppDomain", "Assembly",
            // System enums (for static member access like ConsoleColor.Green)
            "ConsoleColor", "ConsoleKey", "DayOfWeek", "DateTimeKind", "StringComparison",
            "StringSplitOptions", "TypeCode", "MidpointRounding",
            // System.IO
            "File", "Directory", "Path", "FileMode", "FileAccess", "FileShare", "SearchOption",
            // System.Text
            "Encoding", "StringBuilder",
            // System.Threading
            "Task", "Thread", "Monitor", "Interlocked",
            // System.Diagnostics
            "Process", "Stopwatch", "Debug", "Trace"
        };

        private bool IsKnownNetStaticType(string name)
        {
            // Check the hardcoded list first
            if (KnownNetStaticTypes.Contains(name))
                return true;

            // If any .NET namespace is imported and the name looks like a type (PascalCase),
            // treat it as a potential .NET static type
            // This allows System.Windows.Forms, System.Drawing, etc.
            if (!string.IsNullOrEmpty(name) && char.IsUpper(name[0]))
            {
                // Check if the semantic analyzer knows about imported .NET namespaces
                var currentUnit = _semanticAnalyzer?.CurrentUnit;
                if (currentUnit != null && currentUnit.Usings.Any(u => u.IsNetNamespace))
                {
                    return true;
                }
            }

            return false;
        }

        private class LoopContext
        {
            public BasicBlock ContinueTarget { get; }
            public BasicBlock BreakTarget { get; }

            public LoopContext(BasicBlock continueTarget, BasicBlock breakTarget)
            {
                ContinueTarget = continueTarget;
                BreakTarget = breakTarget;
            }
        }
    }
}