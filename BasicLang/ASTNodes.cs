using System;
using System.Collections.Generic;
using BasicLang.Compiler;

namespace BasicLang.Compiler.AST
{
    /// <summary>
    /// Base class for all AST nodes
    /// </summary>
    public abstract class ASTNode
    {
        public int Line { get; set; }
        public int Column { get; set; }
        
        protected ASTNode(int line, int column)
        {
            Line = line;
            Column = column;
        }
        
        public abstract void Accept(IASTVisitor visitor);
    }
    
    /// <summary>
    /// Visitor interface for traversing the AST
    /// </summary>
    public interface IASTVisitor
    {
        void Visit(ProgramNode node);
        void Visit(SubroutineNode node);
        void Visit(FunctionNode node);
        void Visit(ClassNode node);
        void Visit(StructureNode node);
        void Visit(UnionNode node);
        void Visit(TypeNode node);
        void Visit(InterfaceNode node);
        void Visit(EnumNode node);
        void Visit(EnumMemberNode node);
        void Visit(ModuleNode node);
        void Visit(NamespaceNode node);
        void Visit(VariableDeclarationNode node);
        void Visit(TupleDeconstructionNode node);
        void Visit(ConstantDeclarationNode node);
        void Visit(TypeDefineNode node);
        void Visit(ParameterNode node);
        void Visit(BlockNode node);
        void Visit(IfStatementNode node);
        void Visit(SelectStatementNode node);
        void Visit(CaseClauseNode node);
        void Visit(ForLoopNode node);
        void Visit(WhileLoopNode node);
        void Visit(DoLoopNode node);
        void Visit(ForEachLoopNode node);
        void Visit(WithStatementNode node);
        void Visit(ImplicitWithMemberNode node);
        void Visit(TryStatementNode node);
        void Visit(CatchClauseNode node);
        void Visit(ThrowStatementNode node);
        void Visit(ReturnStatementNode node);
        void Visit(ExitStatementNode node);
        void Visit(AssignmentStatementNode node);
        void Visit(ExpressionStatementNode node);
        void Visit(BinaryExpressionNode node);
        void Visit(UnaryExpressionNode node);
        void Visit(LiteralExpressionNode node);
        void Visit(InterpolatedStringNode node);
        void Visit(IdentifierExpressionNode node);
        void Visit(MemberAccessExpressionNode node);
        void Visit(CallExpressionNode node);
        void Visit(ArrayAccessExpressionNode node);
        void Visit(NewExpressionNode node);
        void Visit(CastExpressionNode node);
        void Visit(LambdaExpressionNode node);
        void Visit(TemplateDeclarationNode node);
        void Visit(DelegateDeclarationNode node);
        void Visit(ExtensionMethodNode node);
        void Visit(ExternDeclarationNode node);
        void Visit(UsingDirectiveNode node);
        void Visit(ImportDirectiveNode node);
        void Visit(ConstructorNode node);
        void Visit(PropertyNode node);
        void Visit(MyBaseExpressionNode node);
        void Visit(CollectionInitializerNode node);
        void Visit(TupleLiteralNode node);
        void Visit(OperatorDeclarationNode node);
        void Visit(EventDeclarationNode node);
        void Visit(RaiseEventStatementNode node);
        void Visit(AddHandlerStatementNode node);
        void Visit(RemoveHandlerStatementNode node);
        void Visit(TypePatternNode node);
        void Visit(ConstantPatternNode node);
        void Visit(RangePatternNode node);
        void Visit(ComparisonPatternNode node);
        void Visit(NothingPatternNode node);
        void Visit(OrPatternNode node);
        void Visit(TuplePatternNode node);
        void Visit(BindingPatternNode node);
        void Visit(AwaitExpressionNode node);
        void Visit(YieldStatementNode node);
        void Visit(LinqQueryExpressionNode node);
        void Visit(InlineCodeNode node);
        void Visit(PreprocessorDefineNode node);
        void Visit(PreprocessorUndefineNode node);
        void Visit(PreprocessorIfNode node);
        void Visit(PreprocessorIncludeNode node);
        void Visit(PreprocessorConstNode node);
        void Visit(PreprocessorRegionNode node);
        void Visit(DeclareNode node);
    }
    
    // ============================================================================
    // Program Structure
    // ============================================================================
    
    public class ProgramNode : ASTNode
    {
        public List<ASTNode> Declarations { get; set; }
        
        public ProgramNode(int line, int column) : base(line, column)
        {
            Declarations = new List<ASTNode>();
        }
        
        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    // ============================================================================
    // Type Definitions
    // ============================================================================

    /// <summary>
    /// Generic type parameter constraint kinds
    /// </summary>
    public enum GenericConstraintKind
    {
        None,
        Class,      // Reference type constraint (As Class)
        Structure,  // Value type constraint (As Structure)
        New,        // Constructor constraint (As New)
        Type        // Specific type/interface constraint (As IComparable)
    }

    /// <summary>
    /// Represents a constraint on a generic type parameter
    /// </summary>
    public class GenericConstraint
    {
        public GenericConstraintKind Kind { get; set; }
        public string TypeName { get; set; }  // For Type constraints (interface/class name)

        public GenericConstraint(GenericConstraintKind kind, string typeName = null)
        {
            Kind = kind;
            TypeName = typeName;
        }

        public override string ToString()
        {
            return Kind switch
            {
                GenericConstraintKind.Class => "Class",
                GenericConstraintKind.Structure => "Structure",
                GenericConstraintKind.New => "New",
                GenericConstraintKind.Type => TypeName ?? "?",
                _ => ""
            };
        }
    }

    /// <summary>
    /// Represents a generic type parameter with optional constraints
    /// e.g., T As Class, U As IComparable
    /// </summary>
    public class GenericTypeParameter
    {
        public string Name { get; set; }
        public List<GenericConstraint> Constraints { get; set; }

        public GenericTypeParameter(string name)
        {
            Name = name;
            Constraints = new List<GenericConstraint>();
        }

        public bool HasConstraint(GenericConstraintKind kind)
            => Constraints.Any(c => c.Kind == kind);

        public override string ToString()
        {
            if (Constraints.Count == 0)
                return Name;
            return $"{Name} As {string.Join(", ", Constraints)}";
        }
    }

    public class TypeReference
    {
        public string Name { get; set; }
        public bool IsPointer { get; set; }
        public bool IsArray { get; set; }
        public bool IsNullable { get; set; }
        public bool IsTuple { get; set; }
        public bool IsFixedLengthString { get; set; }
        public int FixedStringLength { get; set; }
        public List<int> ArrayDimensions { get; set; }
        public List<TypeReference> GenericArguments { get; set; }
        public List<TypeReference> TupleElementTypes { get; set; }
        public List<string> TupleElementNames { get; set; }

        public TypeReference(string name)
        {
            Name = name;
            IsPointer = false;
            IsArray = false;
            IsNullable = false;
            IsTuple = false;
            IsFixedLengthString = false;
            FixedStringLength = 0;
            ArrayDimensions = new List<int>();
            GenericArguments = new List<TypeReference>();
            TupleElementTypes = new List<TypeReference>();
            TupleElementNames = new List<string>();
        }
        
        public override string ToString()
        {
            // Handle tuple types
            if (IsTuple && TupleElementTypes.Count > 0)
            {
                var elements = new List<string>();
                for (int i = 0; i < TupleElementTypes.Count; i++)
                {
                    var elementStr = TupleElementTypes[i].ToString();
                    if (i < TupleElementNames.Count && !string.IsNullOrEmpty(TupleElementNames[i]))
                    {
                        elementStr = $"{TupleElementNames[i]} As {elementStr}";
                    }
                    elements.Add(elementStr);
                }
                return $"({string.Join(", ", elements)})";
            }

            var result = Name;

            if (GenericArguments.Count > 0)
            {
                result += $"(Of {string.Join(", ", GenericArguments)})";
            }

            if (IsNullable)
            {
                result += "?";
            }

            if (IsPointer)
            {
                result = $"Pointer To {result}";
            }

            if (IsArray)
            {
                foreach (var dim in ArrayDimensions)
                {
                    result += $"[{(dim >= 0 ? dim.ToString() : "")}]";
                }
            }

            return result;
        }
    }
    
    public class TypeDefineNode : ASTNode
    {
        public string AliasName { get; set; }
        public TypeReference BaseType { get; set; }
        
        public TypeDefineNode(int line, int column) : base(line, column) { }
        
        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class TypeNode : ASTNode
    {
        public string Name { get; set; }
        public List<VariableDeclarationNode> Members { get; set; }
        
        public TypeNode(int line, int column) : base(line, column)
        {
            Members = new List<VariableDeclarationNode>();
        }
        
        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class StructureNode : ASTNode
    {
        public string Name { get; set; }
        public AccessModifier Access { get; set; }
        public List<VariableDeclarationNode> Members { get; set; }

        public StructureNode(int line, int column) : base(line, column)
        {
            Access = AccessModifier.Private;  // Default to Private for multi-file
            Members = new List<VariableDeclarationNode>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    public class UnionNode : ASTNode
    {
        public string Name { get; set; }
        public List<VariableDeclarationNode> Members { get; set; }

        public UnionNode(int line, int column) : base(line, column)
        {
            Members = new List<VariableDeclarationNode>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    // ============================================================================
    // Object-Oriented Programming
    // ============================================================================
    
    public enum AccessModifier
    {
        Public,
        Private,
        Protected,
        Friend,      // Internal to project (like C# internal)
        ProtectedFriend  // Protected OR Friend
    }
    
    public class ClassNode : ASTNode
    {
        public string Name { get; set; }
        public AccessModifier Access { get; set; }
        public List<string> GenericParameters { get; set; }
        public List<GenericTypeParameter> GenericTypeParams { get; set; }  // With constraints
        public string BaseClass { get; set; }
        public List<string> Interfaces { get; set; }
        public List<ASTNode> Members { get; set; }
        public bool IsAbstract { get; set; }  // MustInherit

        public ClassNode(int line, int column) : base(line, column)
        {
            Access = AccessModifier.Private;  // Default to Private for multi-file
            GenericParameters = new List<string>();
            GenericTypeParams = new List<GenericTypeParameter>();
            Interfaces = new List<string>();
            Members = new List<ASTNode>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class InterfaceNode : ASTNode
    {
        public string Name { get; set; }
        public List<FunctionNode> Methods { get; set; }
        public List<PropertyNode> Properties { get; set; }
        public List<string> BaseInterfaces { get; set; }

        public InterfaceNode(int line, int column) : base(line, column)
        {
            Methods = new List<FunctionNode>();
            Properties = new List<PropertyNode>();
            BaseInterfaces = new List<string>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);

        /// <summary>
        /// Check if a method has a default implementation (body)
        /// </summary>
        public bool HasDefaultImplementation(FunctionNode method)
        {
            return method.Body != null && method.Body.Statements.Count > 0;
        }
    }

    public class EnumNode : ASTNode
    {
        public string Name { get; set; }
        public AccessModifier Access { get; set; }
        public TypeReference UnderlyingType { get; set; }
        public List<EnumMemberNode> Members { get; set; }

        public EnumNode(int line, int column) : base(line, column)
        {
            Access = AccessModifier.Public;
            Members = new List<EnumMemberNode>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    public class EnumMemberNode : ASTNode
    {
        public string Name { get; set; }
        public ExpressionNode Value { get; set; }

        public EnumMemberNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    // ============================================================================
    // Modules and Namespaces
    // ============================================================================
    
    public class ModuleNode : ASTNode
    {
        public string Name { get; set; }
        public List<ASTNode> Members { get; set; }
        
        public ModuleNode(int line, int column) : base(line, column)
        {
            Members = new List<ASTNode>();
        }
        
        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class NamespaceNode : ASTNode
    {
        public string Name { get; set; }
        public List<ASTNode> Members { get; set; }
        
        public NamespaceNode(int line, int column) : base(line, column)
        {
            Members = new List<ASTNode>();
        }
        
        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class UsingDirectiveNode : ASTNode
    {
        public string Namespace { get; set; }

        /// <summary>
        /// If true, this is a .NET Framework/BCL namespace import (e.g., System.IO)
        /// If false, this is a BasicLang module import
        /// </summary>
        public bool IsNetNamespace { get; set; }

        /// <summary>
        /// Optional alias for the namespace (e.g., Using IO = System.IO)
        /// </summary>
        public string Alias { get; set; }

        public UsingDirectiveNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class ImportDirectiveNode : ASTNode
    {
        public string Module { get; set; }

        /// <summary>
        /// Path to an external library (e.g., "libs/GameFramework.dll")
        /// Null if importing a project module by name
        /// </summary>
        public string LibraryPath { get; set; }

        /// <summary>
        /// True if this imports an external library rather than a project module
        /// </summary>
        public bool IsExternalLibrary => !string.IsNullOrEmpty(LibraryPath);

        public ImportDirectiveNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    // ============================================================================
    // Functions and Subroutines
    // ============================================================================
    
    public class ParameterNode : ASTNode
    {
        public string Name { get; set; }
        public TypeReference Type { get; set; }
        public ExpressionNode DefaultValue { get; set; }
        public bool IsOptional { get; set; }
        public bool IsParamArray { get; set; }
        public bool IsByRef { get; set; }

        public ParameterNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class SubroutineNode : ASTNode
    {
        public string Name { get; set; }
        public AccessModifier Access { get; set; }
        public List<ParameterNode> Parameters { get; set; }
        public BlockNode Body { get; set; }
        public string ImplementsInterface { get; set; }

        // OOP modifiers
        public bool IsStatic { get; set; }       // Shared
        public bool IsVirtual { get; set; }      // Overridable
        public bool IsOverride { get; set; }     // Overrides
        public bool IsAbstract { get; set; }     // MustOverride
        public bool IsSealed { get; set; }       // NotOverridable

        // Async modifier
        public bool IsAsync { get; set; }

        // Generic parameters for generic subs: Sub Foo(Of T)(...)
        public List<string> GenericParameters { get; set; }
        public List<GenericTypeParameter> GenericTypeParams { get; set; }  // With constraints

        public SubroutineNode(int line, int column) : base(line, column)
        {
            Access = AccessModifier.Public;
            Parameters = new List<ParameterNode>();
            GenericParameters = new List<string>();
            GenericTypeParams = new List<GenericTypeParameter>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    public class FunctionNode : ASTNode
    {
        public string Name { get; set; }
        public AccessModifier Access { get; set; }
        public List<ParameterNode> Parameters { get; set; }
        public TypeReference ReturnType { get; set; }
        public BlockNode Body { get; set; }
        public string ImplementsInterface { get; set; }
        public bool IsAbstract { get; set; }

        // OOP modifiers
        public bool IsStatic { get; set; }       // Shared
        public bool IsVirtual { get; set; }      // Overridable
        public bool IsOverride { get; set; }     // Overrides
        public bool IsSealed { get; set; }       // NotOverridable

        // Async modifier
        public bool IsAsync { get; set; }

        // Iterator modifier
        public bool IsIterator { get; set; }

        // Inline modifier (for header files)
        public bool IsInline { get; set; }

        // Generic parameters for generic functions: Function Foo(Of T)(...)
        public List<string> GenericParameters { get; set; }
        public List<GenericTypeParameter> GenericTypeParams { get; set; }  // With constraints

        public FunctionNode(int line, int column) : base(line, column)
        {
            Access = AccessModifier.Private;  // Default to Private for multi-file
            Parameters = new List<ParameterNode>();
            GenericParameters = new List<string>();
            GenericTypeParams = new List<GenericTypeParameter>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Constructor declaration (Sub New)
    /// </summary>
    public class ConstructorNode : ASTNode
    {
        public AccessModifier Access { get; set; }
        public List<ParameterNode> Parameters { get; set; }
        public BlockNode Body { get; set; }
        public List<ExpressionNode> BaseConstructorArgs { get; set; }  // For MyBase.New(args)

        public ConstructorNode(int line, int column) : base(line, column)
        {
            Access = AccessModifier.Public;
            Parameters = new List<ParameterNode>();
            BaseConstructorArgs = new List<ExpressionNode>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Property declaration with getter and/or setter
    /// </summary>
    public class PropertyNode : ASTNode
    {
        public string Name { get; set; }
        public AccessModifier Access { get; set; }
        public TypeReference PropertyType { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsWriteOnly { get; set; }
        public bool IsStatic { get; set; }       // Shared
        public BlockNode Getter { get; set; }
        public BlockNode Setter { get; set; }
        public ParameterNode SetterParameter { get; set; }  // The 'value' parameter

        public PropertyNode(int line, int column) : base(line, column)
        {
            Access = AccessModifier.Public;
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Operator overload declaration: Operator +(a As Type, b As Type) As Type
    /// </summary>
    public class OperatorDeclarationNode : ASTNode
    {
        public string OperatorSymbol { get; set; }  // +, -, *, /, =, <>, <, >, etc.
        public AccessModifier Access { get; set; }
        public bool IsShared { get; set; }  // Should always be True for operators
        public bool IsWidening { get; set; }  // For conversion operators
        public bool IsNarrowing { get; set; }  // For conversion operators
        public List<ParameterNode> Parameters { get; set; }
        public TypeReference ReturnType { get; set; }
        public BlockNode Body { get; set; }

        public OperatorDeclarationNode(int line, int column) : base(line, column)
        {
            Access = AccessModifier.Public;
            IsShared = true;
            Parameters = new List<ParameterNode>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Event declaration: Public Event Click As EventHandler
    /// </summary>
    public class EventDeclarationNode : ASTNode
    {
        public string Name { get; set; }
        public AccessModifier Access { get; set; }
        public TypeReference EventType { get; set; }  // The delegate type

        public EventDeclarationNode(int line, int column) : base(line, column)
        {
            Access = AccessModifier.Public;
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// RaiseEvent statement: RaiseEvent Click(sender, args)
    /// </summary>
    public class RaiseEventStatementNode : StatementNode
    {
        public string EventName { get; set; }
        public List<ExpressionNode> Arguments { get; set; }

        public RaiseEventStatementNode(int line, int column) : base(line, column)
        {
            Arguments = new List<ExpressionNode>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// AddHandler statement: AddHandler obj.Event, AddressOf Handler
    /// </summary>
    public class AddHandlerStatementNode : StatementNode
    {
        public ExpressionNode EventExpression { get; set; }
        public ExpressionNode HandlerExpression { get; set; }

        public AddHandlerStatementNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// RemoveHandler statement: RemoveHandler obj.Event, AddressOf Handler
    /// </summary>
    public class RemoveHandlerStatementNode : StatementNode
    {
        public ExpressionNode EventExpression { get; set; }
        public ExpressionNode HandlerExpression { get; set; }

        public RemoveHandlerStatementNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    // ============================================================================
    // Templates and Delegates
    // ============================================================================

    public class TemplateDeclarationNode : ASTNode
    {
        public string Name { get; set; }
        public List<string> TypeParameters { get; set; }
        public ASTNode Declaration { get; set; }

        public TemplateDeclarationNode(int line, int column) : base(line, column)
        {
            TypeParameters = new List<string>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class DelegateDeclarationNode : ASTNode
    {
        public string Name { get; set; }
        public List<ParameterNode> Parameters { get; set; }
        public TypeReference ReturnType { get; set; }
        
        public DelegateDeclarationNode(int line, int column) : base(line, column)
        {
            Parameters = new List<ParameterNode>();
        }
        
        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class ExtensionMethodNode : ASTNode
    {
        public string ExtendedType { get; set; }
        public FunctionNode Method { get; set; }

        public ExtensionMethodNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    // ============================================================================
    // LINQ Query Expressions
    // ============================================================================

    /// <summary>
    /// Represents a LINQ query expression: From x In collection Where x > 0 Select x
    /// </summary>
    public class LinqQueryExpressionNode : ExpressionNode
    {
        public List<LinqClause> Clauses { get; set; }

        public LinqQueryExpressionNode(int line, int column) : base(line, column)
        {
            Clauses = new List<LinqClause>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    public abstract class LinqClause
    {
        public int Line { get; set; }
        public int Column { get; set; }
    }

    public class FromClause : LinqClause
    {
        public string VariableName { get; set; }
        public ExpressionNode Collection { get; set; }
    }

    public class WhereClause : LinqClause
    {
        public ExpressionNode Condition { get; set; }
    }

    public class SelectClause : LinqClause
    {
        public ExpressionNode Selector { get; set; }
    }

    public class OrderByClause : LinqClause
    {
        public ExpressionNode KeySelector { get; set; }
        public bool Descending { get; set; }
    }

    public class GroupByClause : LinqClause
    {
        public ExpressionNode KeySelector { get; set; }
        public ExpressionNode ElementSelector { get; set; }
        public string IntoVariable { get; set; }  // For "Into g = Group"
        public bool IsGroupKeyword { get; set; }   // true if "Into g = Group"
    }

    public class JoinClause : LinqClause
    {
        public string VariableName { get; set; }
        public ExpressionNode Collection { get; set; }
        public ExpressionNode OuterKeySelector { get; set; }
        public ExpressionNode InnerKeySelector { get; set; }
        public string IntoVariable { get; set; }  // For group join: "Into g = Group"
    }

    public class AggregateClause : LinqClause
    {
        public string VariableName { get; set; }
        public ExpressionNode Collection { get; set; }
        public ExpressionNode Selector { get; set; }
        public string IntoVariable { get; set; }
    }

    public class LetClause : LinqClause
    {
        public string VariableName { get; set; }
        public ExpressionNode Value { get; set; }
    }

    public class TakeClause : LinqClause
    {
        public ExpressionNode Count { get; set; }
    }

    public class SkipClause : LinqClause
    {
        public ExpressionNode Count { get; set; }
    }

    public class DistinctClause : LinqClause
    {
    }

    /// <summary>
    /// Represents a platform extern declaration
    /// Allows declaring native platform APIs with per-backend implementations
    /// </summary>
    /// <example>
    /// Extern Function MessageBox(hwnd As Integer, text As String) As Integer
    ///     CSharp: "System.Windows.Forms.MessageBox.Show"
    ///     Cpp: "MessageBoxA"
    ///     LLVM: "MessageBoxA"
    /// End Extern
    /// </example>
    public class ExternDeclarationNode : ASTNode
    {
        /// <summary>
        /// True if this is a Function (returns value), false if Sub (void)
        /// </summary>
        public bool IsFunction { get; set; }

        /// <summary>
        /// Name of the extern function/sub
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Parameters for the extern
        /// </summary>
        public List<ParameterNode> Parameters { get; set; }

        /// <summary>
        /// Return type (for functions)
        /// </summary>
        public TypeReference ReturnType { get; set; }

        /// <summary>
        /// Platform-specific implementations
        /// Key: Platform name (CSharp, Cpp, LLVM, MSIL)
        /// Value: The native implementation string
        /// </summary>
        public Dictionary<string, string> PlatformImplementations { get; set; }

        public ExternDeclarationNode(int line, int column) : base(line, column)
        {
            Parameters = new List<ParameterNode>();
            PlatformImplementations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);

        /// <summary>
        /// Get implementation for a specific platform
        /// </summary>
        public string GetImplementation(string platform)
        {
            if (PlatformImplementations.TryGetValue(platform, out var impl))
                return impl;
            return null;
        }

        /// <summary>
        /// Check if this extern has an implementation for the given platform
        /// </summary>
        public bool HasImplementation(string platform)
        {
            return PlatformImplementations.ContainsKey(platform);
        }
    }

    /// <summary>
    /// Represents a VB/FreeBASIC-style Declare statement for C library imports
    /// </summary>
    /// <example>
    /// Declare Function MessageBoxA Lib "user32.dll" Alias "MessageBoxA" (hwnd As Integer, text As String) As Integer
    /// Declare Sub Sleep CDecl Lib "msvcrt.dll" (milliseconds As Integer)
    /// </example>
    public class DeclareNode : ASTNode
    {
        /// <summary>
        /// True if this is a Function (returns value), false if Sub (void)
        /// </summary>
        public bool IsFunction { get; set; }

        /// <summary>
        /// Name of the declared function/sub in BasicLang code
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The library/DLL name (e.g., "user32.dll", "libc.so")
        /// </summary>
        public string LibraryName { get; set; }

        /// <summary>
        /// The actual function name in the library (if different from Name)
        /// </summary>
        public string AliasName { get; set; }

        /// <summary>
        /// Calling convention (CDecl, StdCall, etc.)
        /// </summary>
        public CallingConvention Convention { get; set; }

        /// <summary>
        /// Parameters for the external function
        /// </summary>
        public List<ParameterNode> Parameters { get; set; }

        /// <summary>
        /// Return type (for functions)
        /// </summary>
        public TypeReference ReturnType { get; set; }

        public DeclareNode(int line, int column) : base(line, column)
        {
            Parameters = new List<ParameterNode>();
            Convention = CallingConvention.Default;
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);

        /// <summary>
        /// Gets the actual name to call in the library (AliasName if specified, otherwise Name)
        /// </summary>
        public string GetActualName() => !string.IsNullOrEmpty(AliasName) ? AliasName : Name;
    }

    public enum CallingConvention
    {
        Default,   // Platform default
        CDecl,     // C calling convention
        StdCall,   // Windows standard calling convention
        FastCall,  // Fast calling convention
        ThisCall   // C++ member function calling convention
    }

    // ============================================================================
    // Variable Declarations
    // ============================================================================
    
    public class VariableDeclarationNode : StatementNode
    {
        public string Name { get; set; }
        public TypeReference Type { get; set; }
        public ExpressionNode Initializer { get; set; }
        public AccessModifier Access { get; set; }
        public bool IsAuto { get; set; }
        public bool IsStatic { get; set; }       // Shared
        public bool IsExtern { get; set; }       // Extern (defined elsewhere, for headers)

        public VariableDeclarationNode(int line, int column) : base(line, column)
        {
            Access = AccessModifier.Private;  // Default to Private for multi-file
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Tuple deconstruction statement: Dim (x, y) = GetPair()
    /// </summary>
    public class TupleDeconstructionNode : StatementNode
    {
        /// <summary>
        /// The variables being declared and assigned from the tuple
        /// </summary>
        public List<(string Name, TypeReference Type)> Variables { get; set; }

        /// <summary>
        /// The expression that produces the tuple to deconstruct
        /// </summary>
        public ExpressionNode Initializer { get; set; }

        public TupleDeconstructionNode(int line, int column) : base(line, column)
        {
            Variables = new List<(string, TypeReference)>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    public class ConstantDeclarationNode : StatementNode
    {
        public string Name { get; set; }
        public TypeReference Type { get; set; }
        public ExpressionNode Value { get; set; }
        public AccessModifier Access { get; set; }

        public ConstantDeclarationNode(int line, int column) : base(line, column)
        {
            Access = AccessModifier.Private;
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    // ============================================================================
    // Statements
    // ============================================================================
    
    public abstract class StatementNode : ASTNode
    {
        protected StatementNode(int line, int column) : base(line, column) { }
    }
    
    public class BlockNode : StatementNode
    {
        public List<StatementNode> Statements { get; set; }
        
        public BlockNode(int line, int column) : base(line, column)
        {
            Statements = new List<StatementNode>();
        }
        
        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class IfStatementNode : StatementNode
    {
        public ExpressionNode Condition { get; set; }
        public BlockNode ThenBlock { get; set; }
        public List<(ExpressionNode Condition, BlockNode Block)> ElseIfClauses { get; set; }
        public BlockNode ElseBlock { get; set; }
        
        public IfStatementNode(int line, int column) : base(line, column)
        {
            ElseIfClauses = new List<(ExpressionNode, BlockNode)>();
        }
        
        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class CaseClauseNode : ASTNode
    {
        public List<ExpressionNode> Values { get; set; }
        public List<PatternNode> Patterns { get; set; }  // For pattern matching
        public BlockNode Body { get; set; }
        public bool IsElse { get; set; }

        public CaseClauseNode(int line, int column) : base(line, column)
        {
            Values = new List<ExpressionNode>();
            Patterns = new List<PatternNode>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    // ============================================================================
    // Pattern Matching
    // ============================================================================

    /// <summary>
    /// Base class for patterns in pattern matching
    /// </summary>
    public abstract class PatternNode : ASTNode
    {
        public ExpressionNode WhenGuard { get; set; }  // Optional When clause

        protected PatternNode(int line, int column) : base(line, column) { }
    }

    /// <summary>
    /// Type pattern: Case Is Integer
    /// </summary>
    public class TypePatternNode : PatternNode
    {
        public TypeReference MatchType { get; set; }
        public string VariableName { get; set; }  // Optional binding: Case x As Integer

        public TypePatternNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Constant pattern: Case 1, Case "hello"
    /// </summary>
    public class ConstantPatternNode : PatternNode
    {
        public ExpressionNode Value { get; set; }

        public ConstantPatternNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Range pattern: Case 1 To 10
    /// </summary>
    public class RangePatternNode : PatternNode
    {
        public ExpressionNode LowerBound { get; set; }
        public ExpressionNode UpperBound { get; set; }

        public RangePatternNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Comparison pattern: Case Is > 10
    /// </summary>
    public class ComparisonPatternNode : PatternNode
    {
        public string Operator { get; set; }  // >, <, >=, <=, =, <>
        public ExpressionNode Value { get; set; }

        public ComparisonPatternNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Nothing (null) pattern: Case Nothing
    /// </summary>
    public class NothingPatternNode : PatternNode
    {
        public NothingPatternNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Or pattern: Case 1 Or 2 Or 3 (matches any of the alternatives)
    /// </summary>
    public class OrPatternNode : PatternNode
    {
        public List<PatternNode> Alternatives { get; set; }

        public OrPatternNode(int line, int column) : base(line, column)
        {
            Alternatives = new List<PatternNode>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Tuple/Deconstruction pattern: Case (x, y, z)
    /// </summary>
    public class TuplePatternNode : PatternNode
    {
        public List<PatternNode> Elements { get; set; }

        public TuplePatternNode(int line, int column) : base(line, column)
        {
            Elements = new List<PatternNode>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Binding pattern: Case n When n > 0 (captures value and applies guard)
    /// </summary>
    public class BindingPatternNode : PatternNode
    {
        public string VariableName { get; set; }

        public BindingPatternNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    public class SelectStatementNode : StatementNode
    {
        public ExpressionNode Expression { get; set; }
        public List<CaseClauseNode> Cases { get; set; }

        public SelectStatementNode(int line, int column) : base(line, column)
        {
            Cases = new List<CaseClauseNode>();
        }
        
        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class ForLoopNode : StatementNode
    {
        public string Variable { get; set; }
        public string VariableType { get; set; }  // Optional inline type declaration (For i As Integer)
        public ExpressionNode Start { get; set; }
        public ExpressionNode End { get; set; }
        public ExpressionNode Step { get; set; }
        public BlockNode Body { get; set; }

        public ForLoopNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class WhileLoopNode : StatementNode
    {
        public ExpressionNode Condition { get; set; }
        public BlockNode Body { get; set; }
        
        public WhileLoopNode(int line, int column) : base(line, column) { }
        
        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class DoLoopNode : StatementNode
    {
        public ExpressionNode Condition { get; set; }
        public BlockNode Body { get; set; }
        public bool IsWhile { get; set; }  // true = While, false = Until
        public bool IsConditionAtStart { get; set; }  // true = Do While/Until...Loop, false = Do...Loop While/Until

        public DoLoopNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class ForEachLoopNode : StatementNode
    {
        public string Variable { get; set; }
        public TypeReference VariableType { get; set; }
        public ExpressionNode Collection { get; set; }
        public BlockNode Body { get; set; }

        public ForEachLoopNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// With block: With expr ... End With
    /// </summary>
    public class WithStatementNode : StatementNode
    {
        public ExpressionNode Object { get; set; }
        public BlockNode Body { get; set; }

        public WithStatementNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Implicit With member access: .Member inside a With block
    /// </summary>
    public class ImplicitWithMemberNode : ExpressionNode
    {
        public string MemberName { get; set; }

        public ImplicitWithMemberNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    public class CatchClauseNode : ASTNode
    {
        public string ExceptionVariable { get; set; }
        public TypeReference ExceptionType { get; set; }
        public BlockNode Body { get; set; }
        
        public CatchClauseNode(int line, int column) : base(line, column) { }
        
        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class TryStatementNode : StatementNode
    {
        public BlockNode TryBlock { get; set; }
        public List<CatchClauseNode> CatchClauses { get; set; }
        public BlockNode FinallyBlock { get; set; }

        public TryStatementNode(int line, int column) : base(line, column)
        {
            CatchClauses = new List<CatchClauseNode>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    public class ThrowStatementNode : StatementNode
    {
        public ExpressionNode Exception { get; set; }

        public ThrowStatementNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class ReturnStatementNode : StatementNode
    {
        public ExpressionNode Value { get; set; }

        public ReturnStatementNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    public enum ExitKind { For, Do, While, Sub, Function }

    public class ExitStatementNode : StatementNode
    {
        public ExitKind Kind { get; set; }

        public ExitStatementNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    public class AssignmentStatementNode : StatementNode
    {
        public ExpressionNode Target { get; set; }
        public string Operator { get; set; }
        public ExpressionNode Value { get; set; }
        
        public AssignmentStatementNode(int line, int column) : base(line, column) { }
        
        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class ExpressionStatementNode : StatementNode
    {
        public ExpressionNode Expression { get; set; }
        
        public ExpressionStatementNode(int line, int column) : base(line, column) { }
        
        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    // ============================================================================
    // Expressions
    // ============================================================================
    
    public abstract class ExpressionNode : ASTNode
    {
        protected ExpressionNode(int line, int column) : base(line, column) { }
    }
    
    public class BinaryExpressionNode : ExpressionNode
    {
        public ExpressionNode Left { get; set; }
        public string Operator { get; set; }
        public ExpressionNode Right { get; set; }
        
        public BinaryExpressionNode(int line, int column) : base(line, column) { }
        
        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class UnaryExpressionNode : ExpressionNode
    {
        public string Operator { get; set; }
        public ExpressionNode Operand { get; set; }
        public bool IsPostfix { get; set; }
        
        public UnaryExpressionNode(int line, int column) : base(line, column) { }
        
        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class LiteralExpressionNode : ExpressionNode
    {
        public object Value { get; set; }
        public TokenType LiteralType { get; set; }

        public LiteralExpressionNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Interpolated string: $"Hello {name}, you are {age} years old"
    /// </summary>
    public class InterpolatedStringNode : ExpressionNode
    {
        /// <summary>
        /// Parts of the interpolated string. Each part is either:
        /// - A string (literal text)
        /// - An ExpressionNode (interpolated expression)
        /// </summary>
        public List<object> Parts { get; set; }

        public InterpolatedStringNode(int line, int column) : base(line, column)
        {
            Parts = new List<object>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    public class IdentifierExpressionNode : ExpressionNode
    {
        public string Name { get; set; }

        public IdentifierExpressionNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Represents the MyBase keyword for accessing base class members
    /// </summary>
    public class MyBaseExpressionNode : ExpressionNode
    {
        public MyBaseExpressionNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    public class MemberAccessExpressionNode : ExpressionNode
    {
        public ExpressionNode Object { get; set; }
        public string MemberName { get; set; }
        /// <summary>
        /// Indicates the member access is incomplete (e.g., "obj." with no member name yet).
        /// Used for IntelliSense to provide completions when user is typing after a dot.
        /// </summary>
        public bool IsIncomplete { get; set; }

        public MemberAccessExpressionNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class CallExpressionNode : ExpressionNode
    {
        public ExpressionNode Callee { get; set; }
        public List<ExpressionNode> Arguments { get; set; }
        public List<TypeReference> GenericArguments { get; set; }

        public CallExpressionNode(int line, int column) : base(line, column)
        {
            Arguments = new List<ExpressionNode>();
            GenericArguments = new List<TypeReference>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class ArrayAccessExpressionNode : ExpressionNode
    {
        public ExpressionNode Array { get; set; }
        public List<ExpressionNode> Indices { get; set; }
        
        public ArrayAccessExpressionNode(int line, int column) : base(line, column)
        {
            Indices = new List<ExpressionNode>();
        }
        
        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class NewExpressionNode : ExpressionNode
    {
        public TypeReference Type { get; set; }
        public List<ExpressionNode> Arguments { get; set; }
        
        public NewExpressionNode(int line, int column) : base(line, column)
        {
            Arguments = new List<ExpressionNode>();
        }
        
        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
    
    public class CastExpressionNode : ExpressionNode
    {
        public ExpressionNode Expression { get; set; }
        public TypeReference TargetType { get; set; }

        public CastExpressionNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Lambda expression: Function(x) x * 2 or Sub(x) DoSomething(x)
    /// </summary>
    public class LambdaExpressionNode : ExpressionNode
    {
        public List<ParameterNode> Parameters { get; set; }
        public ExpressionNode Body { get; set; }          // For expression lambdas
        public BlockNode StatementBody { get; set; }      // For statement lambdas
        public TypeReference ReturnType { get; set; }     // Optional return type
        public bool IsFunction { get; set; }              // True for Function, false for Sub

        public LambdaExpressionNode(int line, int column) : base(line, column)
        {
            Parameters = new List<ParameterNode>();
            IsFunction = true;
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Collection initializer expression: { 1, 2, 3 }
    /// </summary>
    public class CollectionInitializerNode : ExpressionNode
    {
        public List<ExpressionNode> Elements { get; set; }
        public TypeReference ElementType { get; set; }  // Inferred or explicit element type

        public CollectionInitializerNode(int line, int column) : base(line, column)
        {
            Elements = new List<ExpressionNode>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Tuple literal expression: (1, "hello") or (x := 1, y := 2)
    /// </summary>
    public class TupleLiteralNode : ExpressionNode
    {
        public List<ExpressionNode> Elements { get; set; }
        public List<string> ElementNames { get; set; }  // Optional named elements

        public TupleLiteralNode(int line, int column) : base(line, column)
        {
            Elements = new List<ExpressionNode>();
            ElementNames = new List<string>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Await expression: Await SomeAsyncMethod()
    /// </summary>
    public class AwaitExpressionNode : ExpressionNode
    {
        public ExpressionNode Expression { get; set; }

        public AwaitExpressionNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Yield statement: Yield value or Yield Return value
    /// </summary>
    public class YieldStatementNode : StatementNode
    {
        public ExpressionNode Value { get; set; }
        public bool IsBreak { get; set; }  // Yield Break vs Yield Return

        public YieldStatementNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>
    /// Inline code block for embedding native code: csharp{ ... }, cpp{ ... }, llvm{ ... }, msil{ ... }
    /// </summary>
    public class InlineCodeNode : StatementNode
    {
        public string Language { get; set; }  // "csharp", "cpp", "llvm", "msil"
        public string Code { get; set; }       // The raw code content

        public InlineCodeNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    // ============================================================================
    // Preprocessor Directives
    // ============================================================================

    public class PreprocessorDefineNode : ASTNode
    {
        public string Name { get; set; }
        public string Value { get; set; }  // Optional value for the definition

        public PreprocessorDefineNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    public class PreprocessorUndefineNode : ASTNode
    {
        public string Name { get; set; }

        public PreprocessorUndefineNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    public class PreprocessorIfNode : ASTNode
    {
        public ExpressionNode Condition { get; set; }
        public List<ASTNode> ThenBody { get; set; }
        public List<PreprocessorElseIfClause> ElseIfClauses { get; set; }
        public List<ASTNode> ElseBody { get; set; }

        public PreprocessorIfNode(int line, int column) : base(line, column)
        {
            ThenBody = new List<ASTNode>();
            ElseIfClauses = new List<PreprocessorElseIfClause>();
            ElseBody = new List<ASTNode>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    public class PreprocessorElseIfClause
    {
        public ExpressionNode Condition { get; set; }
        public List<ASTNode> Body { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }

        public PreprocessorElseIfClause(int line, int column)
        {
            Body = new List<ASTNode>();
            Line = line;
            Column = column;
        }
    }

    public class PreprocessorIncludeNode : ASTNode
    {
        public string FilePath { get; set; }

        public PreprocessorIncludeNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    public class PreprocessorConstNode : ASTNode
    {
        public string Name { get; set; }
        public ExpressionNode Value { get; set; }

        public PreprocessorConstNode(int line, int column) : base(line, column) { }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }

    public class PreprocessorRegionNode : ASTNode
    {
        public string Name { get; set; }
        public List<ASTNode> Body { get; set; }

        public PreprocessorRegionNode(int line, int column) : base(line, column)
        {
            Body = new List<ASTNode>();
        }

        public override void Accept(IASTVisitor visitor) => visitor.Visit(this);
    }
}
