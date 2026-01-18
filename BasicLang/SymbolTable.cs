using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.AST;
using BasicLang.Compiler;

namespace BasicLang.Compiler.SemanticAnalysis
{
    /// <summary>
    /// Represents a type in the BasicLang type system
    /// </summary>

public class TypeInfo
    {
        public string Name { get; set; }
        public TypeKind Kind { get; set; }
        public TypeInfo BaseType { get; set; }
        public List<TypeInfo> Interfaces { get; set; }
        public List<TypeInfo> GenericArguments { get; set; }
        public TypeInfo ElementType { get; set; } // For arrays
        public TypeInfo UnderlyingType { get; set; } // For nullable types
        public List<TypeInfo> TupleElementTypes { get; set; } // For tuples
        public List<string> TupleElementNames { get; set; } // For named tuple elements
        public int ArrayRank { get; set; } // Number of dimensions
        public int ArraySize { get; set; } // Size of array (for fixed-size arrays)
        public bool IsPointer { get; set; }
        public bool IsNullable { get; set; }
        public bool IsFixedLengthString { get; set; }
        public int FixedStringLength { get; set; } // For fixed-length strings
        public bool IsAbstract { get; set; } // For abstract classes
        public Dictionary<string, Symbol> Members { get; set; }

        public TypeInfo(string name, TypeKind kind)
        {
            Name = name;
            Kind = kind;
            Interfaces = new List<TypeInfo>();
            GenericArguments = new List<TypeInfo>();
            TupleElementTypes = new List<TypeInfo>();
            TupleElementNames = new List<string>();
            Members = new Dictionary<string, Symbol>();
        }
        
        public bool IsNumeric()
        {
            return Name == "Integer" || Name == "Long" || Name == "Single" || Name == "Double" ||
                   Name == "Byte" || Name == "Short" || Name == "UByte" || Name == "UShort" ||
                   Name == "UInteger" || Name == "ULong";
        }

        public bool IsIntegral()
        {
            return Name == "Integer" || Name == "Long" || Name == "Byte" || Name == "Short" ||
                   Name == "UByte" || Name == "UShort" || Name == "UInteger" || Name == "ULong";
        }

        public bool IsUnsigned()
        {
            return Name == "UByte" || Name == "UShort" || Name == "UInteger" || Name == "ULong";
        }

        public bool IsSigned()
        {
            return Name == "Byte" || Name == "Short" || Name == "Integer" || Name == "Long";
        }
        
        public bool IsFloatingPoint()
        {
            return Name == "Single" || Name == "Double";
        }
        
        public bool IsAssignableFrom(TypeInfo other)
        {
            if (Equals(other))
                return true;

            // Object can accept any type (boxing)
            if (Name == "Object")
                return true;

            // Numeric conversions
            if (IsNumeric() && other.IsNumeric())
            {
                // Allow implicit conversions that don't lose precision
                if (Name == "Double" && (other.Name == "Single" || other.IsIntegral()))
                    return true;
                if (Name == "Single" && other.IsIntegral())
                    return true;
                if (Name == "Long" && other.Name == "Integer")
                    return true;
            }
            
            // Check inheritance
            var current = other.BaseType;
            while (current != null)
            {
                if (Equals(current))
                    return true;
                current = current.BaseType;
            }
            
            // Check interfaces
            if (Kind == TypeKind.Interface)
            {
                return other.Interfaces.Any(i => Equals(i));
            }
            
            return false;
        }
        
        public bool Equals(TypeInfo other)
        {
            if (other == null)
                return false;
                
            if (Name != other.Name)
                return false;
                
            if (IsPointer != other.IsPointer)
                return false;
                
            if (ArrayRank != other.ArrayRank)
                return false;
                
            if (GenericArguments.Count != other.GenericArguments.Count)
                return false;
                
            for (int i = 0; i < GenericArguments.Count; i++)
            {
                if (!GenericArguments[i].Equals(other.GenericArguments[i]))
                    return false;
            }
            
            return true;
        }
        
        public override string ToString()
        {
            var result = Name;

            if (GenericArguments.Count > 0)
            {
                result += $"<{string.Join(", ", GenericArguments.Select(t => t.ToString()))}>";
            }

            if (IsPointer)
            {
                result = $"Pointer To {result}";
            }

            // Only add array brackets if Name doesn't already end with brackets
            if (ArrayRank > 0 && !result.EndsWith("]"))
            {
                result += new string('[', ArrayRank) + new string(']', ArrayRank);
            }

            return result;
        }
    }
    
    public enum TypeKind
    {
        Primitive,      // Integer, String, etc.
        Class,
        Interface,
        Structure,
        Union,          // Union type (like C union)
        Enum,
        UserDefinedType,
        Delegate,
        TypeParameter,  // Generic type parameter
        Array,
        Pointer,
        Nullable,       // Nullable value type
        Tuple,          // Tuple type
        Void
    }
    
    /// <summary>
    /// Represents a symbol in the symbol table
    /// </summary>
    public class Symbol
    {
        public string Name { get; set; }
        public SymbolKind Kind { get; set; }
        public TypeInfo Type { get; set; }
        public Scope DeclaringScope { get; set; }
        public bool IsConstant { get; set; }
        public bool IsDefined { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        
        // For functions/methods
        public List<Symbol> Parameters { get; set; }
        public TypeInfo ReturnType { get; set; }

        // For parameters
        public bool IsOptional { get; set; }
        public bool IsParamArray { get; set; }
        public bool IsByRef { get; set; }
        
        // For classes
        public List<TypeInfo> GenericParameters { get; set; }
        
        // Access control
        public AccessModifier Access { get; set; }

        // For extern declarations
        public bool IsExtern { get; set; }
        public Dictionary<string, string> ExternImplementations { get; set; }

        // For imported symbols (from other modules)
        public bool IsImported { get; set; }
        public string SourceModule { get; set; }

        // For external library symbols
        public bool IsPublic { get; set; }
        public string ExternLibrary { get; set; }
        public string ExternAlias { get; set; }
        public List<NetMemberInfo> NetMembers { get; set; }
        public Type NetType { get; set; }

        public Symbol(string name, SymbolKind kind) : this(name, kind, null, 0, 0)
        {
        }

        public Symbol(string name, SymbolKind kind, TypeInfo type, int line, int column)
        {
            Name = name;
            Kind = kind;
            Type = type;
            Line = line;
            Column = column;
            Parameters = new List<Symbol>();
            GenericParameters = new List<TypeInfo>();
            IsDefined = true;
            Access = AccessModifier.Public;
        }
        
        public override string ToString()
        {
            return $"{Kind} {Name} : {Type}";
        }
    }
    
    public enum SymbolKind
    {
        Variable,
        Constant,
        Parameter,
        Function,
        Subroutine,
        Class,
        Interface,
        Structure,
        Type,
        Module,
        Namespace,
        TypeParameter,
        Property,
        Event
    }
    
    /// <summary>
    /// Represents a scope in the program
    /// </summary>
    public class Scope
    {
        public string Name { get; set; }
        public ScopeKind Kind { get; set; }
        public Scope Parent { get; set; }
        public List<Scope> Children { get; set; }
        public Dictionary<string, Symbol> Symbols { get; set; }
        
        // For function/method scopes
        public TypeInfo ReturnType { get; set; }
        
        // For class scopes
        public TypeInfo ClassType { get; set; }
        
        public Scope(string name, ScopeKind kind, Scope parent)
        {
            Name = name;
            Kind = kind;
            Parent = parent;
            Children = new List<Scope>();
            Symbols = new Dictionary<string, Symbol>(StringComparer.OrdinalIgnoreCase);
            
            if (parent != null)
            {
                parent.Children.Add(this);
            }
        }
        
        public bool Define(Symbol symbol)
        {
            if (Symbols.ContainsKey(symbol.Name))
            {
                return false; // Already defined
            }
            
            Symbols[symbol.Name] = symbol;
            symbol.DeclaringScope = this;
            return true;
        }
        
        public Symbol Resolve(string name)
        {
            if (Symbols.TryGetValue(name, out var symbol))
            {
                return symbol;
            }
            
            // Look in parent scope
            if (Parent != null)
            {
                return Parent.Resolve(name);
            }
            
            return null;
        }
        
        public Symbol ResolveLocal(string name)
        {
            Symbols.TryGetValue(name, out var symbol);
            return symbol;
        }
        
        public bool IsInLoop()
        {
            if (Kind == ScopeKind.Loop)
                return true;
                
            if (Parent != null)
                return Parent.IsInLoop();
                
            return false;
        }
        
        public bool IsInFunction()
        {
            if (Kind == ScopeKind.Function || Kind == ScopeKind.Subroutine)
                return true;
                
            if (Parent != null)
                return Parent.IsInFunction();
                
            return false;
        }
        
        public Scope GetFunctionScope()
        {
            if (Kind == ScopeKind.Function || Kind == ScopeKind.Subroutine)
                return this;
                
            if (Parent != null)
                return Parent.GetFunctionScope();
                
            return null;
        }
        
        public Scope GetClassScope()
        {
            if (Kind == ScopeKind.Class)
                return this;

            if (Parent != null)
                return Parent.GetClassScope();

            return null;
        }

        public Scope GetLoopScope()
        {
            if (Kind == ScopeKind.Loop)
                return this;

            if (Parent != null)
                return Parent.GetLoopScope();

            return null;
        }

        public override string ToString()
        {
            return $"{Kind} Scope: {Name} ({Symbols.Count} symbols)";
        }
    }
    
    public enum ScopeKind
    {
        Global,
        Namespace,
        Module,
        Class,
        Interface,
        Function,
        Subroutine,
        Block,
        Loop
    }
    
    /// <summary>
    /// Manages the type system
    /// </summary>
    public class TypeManager
    {
        private readonly Dictionary<string, TypeInfo> _types;
        
        public TypeInfo IntegerType { get; }
        public TypeInfo LongType { get; }
        public TypeInfo SingleType { get; }
        public TypeInfo DoubleType { get; }
        public TypeInfo StringType { get; }
        public TypeInfo BooleanType { get; }
        public TypeInfo CharType { get; }
        public TypeInfo VoidType { get; }
        public TypeInfo ObjectType { get; }

        // Additional numeric types
        public TypeInfo ByteType { get; }
        public TypeInfo ShortType { get; }
        public TypeInfo UByteType { get; }
        public TypeInfo UShortType { get; }
        public TypeInfo UIntegerType { get; }
        public TypeInfo ULongType { get; }

        public TypeInfo ExceptionType { get; }

        public TypeManager()
        {
            _types = new Dictionary<string, TypeInfo>(StringComparer.OrdinalIgnoreCase);

            // Define built-in types
            IntegerType = DefineBuiltInType("Integer", TypeKind.Primitive);
            LongType = DefineBuiltInType("Long", TypeKind.Primitive);
            SingleType = DefineBuiltInType("Single", TypeKind.Primitive);
            DoubleType = DefineBuiltInType("Double", TypeKind.Primitive);
            StringType = DefineBuiltInType("String", TypeKind.Primitive);
            BooleanType = DefineBuiltInType("Boolean", TypeKind.Primitive);
            CharType = DefineBuiltInType("Char", TypeKind.Primitive);
            VoidType = DefineBuiltInType("Void", TypeKind.Void);
            ObjectType = DefineBuiltInType("Object", TypeKind.Class);
            ExceptionType = DefineBuiltInType("Exception", TypeKind.Class);

            // Additional numeric types (signed)
            ByteType = DefineBuiltInType("Byte", TypeKind.Primitive);
            ShortType = DefineBuiltInType("Short", TypeKind.Primitive);

            // Unsigned types
            UByteType = DefineBuiltInType("UByte", TypeKind.Primitive);
            UShortType = DefineBuiltInType("UShort", TypeKind.Primitive);
            UIntegerType = DefineBuiltInType("UInteger", TypeKind.Primitive);
            ULongType = DefineBuiltInType("ULong", TypeKind.Primitive);

            // Add members to Exception type
            ExceptionType.Members["Message"] = new Symbol("Message", SymbolKind.Property, StringType, 0, 0);
            ExceptionType.Members["StackTrace"] = new Symbol("StackTrace", SymbolKind.Property, StringType, 0, 0);
            ExceptionType.Members["InnerException"] = new Symbol("InnerException", SymbolKind.Property, ExceptionType, 0, 0);
        }
        
        private TypeInfo DefineBuiltInType(string name, TypeKind kind)
        {
            var type = new TypeInfo(name, kind);
            _types[name] = type;
            return type;
        }
        
        public TypeInfo DefineType(string name, TypeKind kind)
        {
            if (_types.ContainsKey(name))
            {
                return null; // Already defined
            }
            
            var type = new TypeInfo(name, kind);
            _types[name] = type;
            return type;
        }
        
        public TypeInfo GetType(string name)
        {
            _types.TryGetValue(name, out var type);
            return type;
        }
        
        public TypeInfo CreateArrayType(TypeInfo elementType, int rank, int size = 0)
        {
            var name = $"{elementType.Name}[]";

            var arrayType = new TypeInfo(name, TypeKind.Array)
            {
                ElementType = elementType,
                ArrayRank = rank,
                ArraySize = size
            };

            return arrayType;
        }
        
        public TypeInfo CreatePointerType(TypeInfo targetType)
        {
            var pointerType = new TypeInfo($"Pointer To {targetType.Name}", TypeKind.Pointer)
            {
                IsPointer = true,
                ElementType = targetType  // The type the pointer points to
            };

            return pointerType;
        }
        
        public TypeInfo CreateGenericType(string name, List<TypeInfo> typeArguments)
        {
            var baseType = GetType(name);
            if (baseType == null)
                return null;
                
            var genericType = new TypeInfo(name, baseType.Kind);
            genericType.GenericArguments.AddRange(typeArguments);
            genericType.BaseType = baseType.BaseType;
            
            // Copy members
            foreach (var kvp in baseType.Members)
            {
                genericType.Members[kvp.Key] = kvp.Value;
            }
            
            return genericType;
        }
        
        public bool AreCompatible(TypeInfo left, TypeInfo right)
        {
            return left.IsAssignableFrom(right);
        }
        
        public TypeInfo GetCommonType(TypeInfo left, TypeInfo right)
        {
            if (left.Equals(right))
                return left;
                
            // Numeric promotions
            if (left.IsNumeric() && right.IsNumeric())
            {
                if (left.Name == "Double" || right.Name == "Double")
                    return DoubleType;
                if (left.Name == "Single" || right.Name == "Single")
                    return SingleType;
                if (left.Name == "Long" || right.Name == "Long")
                    return LongType;
                return IntegerType;
            }
            
            // Check if one is assignable to the other
            if (left.IsAssignableFrom(right))
                return left;
            if (right.IsAssignableFrom(left))
                return right;
                
            // Find common base class
            var leftHierarchy = new List<TypeInfo>();
            var current = left;
            while (current != null)
            {
                leftHierarchy.Add(current);
                current = current.BaseType;
            }
            
            current = right;
            while (current != null)
            {
                if (leftHierarchy.Contains(current))
                    return current;
                current = current.BaseType;
            }
            
            return ObjectType;
        }
    }
    
    /// <summary>
    /// Semantic error information
    /// </summary>
    public class SemanticError
    {
        public string Message { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public ErrorSeverity Severity { get; set; }
        public string ErrorCode { get; set; }
        public string Suggestion { get; set; }
        public string ExpectedType { get; set; }
        public string ActualType { get; set; }

        public SemanticError(string message, int line, int column, ErrorSeverity severity = ErrorSeverity.Error)
        {
            Message = message;
            Line = line;
            Column = column;
            Severity = severity;
        }

        /// <summary>
        /// Create an error with a suggestion for fixing it
        /// </summary>
        public static SemanticError WithSuggestion(string message, int line, int column, string suggestion)
        {
            return new SemanticError(message, line, column) { Suggestion = suggestion };
        }

        /// <summary>
        /// Create a type mismatch error with expected and actual types
        /// </summary>
        public static SemanticError TypeMismatch(string message, int line, int column, string expectedType, string actualType)
        {
            return new SemanticError(message, line, column)
            {
                ErrorCode = "BL3001",
                ExpectedType = expectedType,
                ActualType = actualType
            };
        }

        /// <summary>
        /// Create an undefined symbol error
        /// </summary>
        public static SemanticError UndefinedSymbol(string symbolName, int line, int column, string suggestion = null)
        {
            var msg = $"Undefined identifier '{symbolName}'";
            if (!string.IsNullOrEmpty(suggestion))
                msg += $". Did you mean '{suggestion}'?";
            return new SemanticError(msg, line, column)
            {
                ErrorCode = "BL3002",
                Suggestion = suggestion != null ? $"Replace '{symbolName}' with '{suggestion}'" : null
            };
        }

        public override string ToString()
        {
            var result = $"{Severity} at line {Line}, column {Column}: {Message}";
            if (!string.IsNullOrEmpty(Suggestion))
                result += $"\n  Suggestion: {Suggestion}";
            return result;
        }
    }

    public enum ErrorSeverity
    {
        Warning,
        Error
    }
}
