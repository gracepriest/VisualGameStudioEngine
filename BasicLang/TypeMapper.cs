using System;
using System.Collections.Generic;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen
{
    /// <summary>
    /// Base implementation of ITypeMapper with common functionality
    /// </summary>
    public abstract class TypeMapperBase : ITypeMapper
    {
        protected readonly Dictionary<string, string> _typeMap;
        protected readonly Dictionary<BinaryOpKind, string> _binaryOpMap;
        protected readonly Dictionary<CompareKind, string> _compareOpMap;
        protected readonly Dictionary<UnaryOpKind, string> _unaryOpMap;

        protected TypeMapperBase()
        {
            _typeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _binaryOpMap = new Dictionary<BinaryOpKind, string>();
            _compareOpMap = new Dictionary<CompareKind, string>();
            _unaryOpMap = new Dictionary<UnaryOpKind, string>();

            InitializeTypeMappings();
            InitializeBinaryOperators();
            InitializeCompareOperators();
            InitializeUnaryOperators();
        }

        protected abstract void InitializeTypeMappings();
        protected abstract void InitializeBinaryOperators();
        protected abstract void InitializeCompareOperators();
        protected abstract void InitializeUnaryOperators();

        public virtual string MapType(TypeInfo type)
        {
            if (type == null) return GetDefaultType();

            // Handle arrays
            if (type.Kind == TypeKind.Array && type.ElementType != null)
            {
                return MapArrayType(type);
            }

            // Handle pointers
            if (type.Kind == TypeKind.Pointer || type.IsPointer)
            {
                return MapPointerType(type);
            }

            // Handle basic types
            if (_typeMap.TryGetValue(type.Name, out var mapped))
                return mapped;

            return type.Name;
        }

        protected abstract string GetDefaultType();
        protected abstract string MapArrayType(TypeInfo type);
        protected abstract string MapPointerType(TypeInfo type);

        public virtual string MapBinaryOperator(BinaryOpKind op)
        {
            if (_binaryOpMap.TryGetValue(op, out var mapped))
                return mapped;
            throw new NotSupportedException($"Binary operator not supported: {op}");
        }

        public virtual string MapComparisonOperator(CompareKind op)
        {
            if (_compareOpMap.TryGetValue(op, out var mapped))
                return mapped;
            throw new NotSupportedException($"Comparison operator not supported: {op}");
        }

        public virtual string MapUnaryOperator(UnaryOpKind op)
        {
            if (_unaryOpMap.TryGetValue(op, out var mapped))
                return mapped;
            throw new NotSupportedException($"Unary operator not supported: {op}");
        }

        public abstract string GetDefaultValue(TypeInfo type);
    }

    /// <summary>
    /// Type mapper for C# backend
    /// </summary>
    public class CSharpTypeMapper : TypeMapperBase
    {
        protected override void InitializeTypeMappings()
        {
            _typeMap["Integer"] = "int";
            _typeMap["Long"] = "long";
            _typeMap["Single"] = "float";
            _typeMap["Double"] = "double";
            _typeMap["String"] = "string";
            _typeMap["Boolean"] = "bool";
            _typeMap["Char"] = "char";
            _typeMap["Void"] = "void";
            _typeMap["Object"] = "object";
            _typeMap["Byte"] = "sbyte";
            _typeMap["Short"] = "short";
            _typeMap["UByte"] = "byte";
            _typeMap["UShort"] = "ushort";
            _typeMap["UInteger"] = "uint";
            _typeMap["ULong"] = "ulong";
            _typeMap["Decimal"] = "decimal";
        }

        protected override void InitializeBinaryOperators()
        {
            // Arithmetic
            _binaryOpMap[BinaryOpKind.Add] = "+";
            _binaryOpMap[BinaryOpKind.Sub] = "-";
            _binaryOpMap[BinaryOpKind.Mul] = "*";
            _binaryOpMap[BinaryOpKind.Div] = "/";
            _binaryOpMap[BinaryOpKind.Mod] = "%";
            _binaryOpMap[BinaryOpKind.IntDiv] = "/";

            // Bitwise
            _binaryOpMap[BinaryOpKind.And] = "&";
            _binaryOpMap[BinaryOpKind.Or] = "|";
            _binaryOpMap[BinaryOpKind.Xor] = "^";
            _binaryOpMap[BinaryOpKind.Shl] = "<<";
            _binaryOpMap[BinaryOpKind.Shr] = ">>";

            // Comparison (when used in binary op context)
            _binaryOpMap[BinaryOpKind.Eq] = "==";
            _binaryOpMap[BinaryOpKind.Ne] = "!=";
            _binaryOpMap[BinaryOpKind.Lt] = "<";
            _binaryOpMap[BinaryOpKind.Le] = "<=";
            _binaryOpMap[BinaryOpKind.Gt] = ">";
            _binaryOpMap[BinaryOpKind.Ge] = ">=";

            // String
            _binaryOpMap[BinaryOpKind.Concat] = "+";
        }

        protected override void InitializeCompareOperators()
        {
            _compareOpMap[CompareKind.Eq] = "==";
            _compareOpMap[CompareKind.Ne] = "!=";
            _compareOpMap[CompareKind.Lt] = "<";
            _compareOpMap[CompareKind.Le] = "<=";
            _compareOpMap[CompareKind.Gt] = ">";
            _compareOpMap[CompareKind.Ge] = ">=";
        }

        protected override void InitializeUnaryOperators()
        {
            _unaryOpMap[UnaryOpKind.Neg] = "-";
            _unaryOpMap[UnaryOpKind.Not] = "!";
            _unaryOpMap[UnaryOpKind.BitwiseNot] = "~";
            _unaryOpMap[UnaryOpKind.Inc] = "++";
            _unaryOpMap[UnaryOpKind.Dec] = "--";
        }

        protected override string GetDefaultType() => "object";

        protected override string MapArrayType(TypeInfo type)
        {
            var elementType = MapType(type.ElementType);
            return $"{elementType}[]";
        }

        protected override string MapPointerType(TypeInfo type)
        {
            var baseType = type.ElementType != null ? MapType(type.ElementType) : "void";
            return $"{baseType}*";
        }

        public override string GetDefaultValue(TypeInfo type)
        {
            if (type == null) return "null";

            var typeName = type.Name?.ToLower() ?? "";

            return typeName switch
            {
                "integer" or "int" => "0",
                "long" => "0L",
                "single" or "float" => "0f",
                "double" => "0.0",
                "boolean" or "bool" => "false",
                "char" => "'\\0'",
                "string" => "\"\"",
                "byte" or "ubyte" => "(byte)0",
                "short" or "ushort" => "(short)0",
                "uinteger" => "0U",
                "ulong" => "0UL",
                _ => type.Kind == TypeKind.Primitive ? "default" : "null"
            };
        }
    }

    /// <summary>
    /// Type mapper for C++ backend
    /// </summary>
    public class CppTypeMapper : TypeMapperBase
    {
        protected override void InitializeTypeMappings()
        {
            _typeMap["Integer"] = "int32_t";
            _typeMap["Long"] = "int64_t";
            _typeMap["Single"] = "float";
            _typeMap["Double"] = "double";
            _typeMap["String"] = "std::string";
            _typeMap["Boolean"] = "bool";
            _typeMap["Char"] = "char";
            _typeMap["Void"] = "void";
            _typeMap["Object"] = "void*";
            _typeMap["Byte"] = "int8_t";
            _typeMap["Short"] = "int16_t";
            _typeMap["UByte"] = "uint8_t";
            _typeMap["UShort"] = "uint16_t";
            _typeMap["UInteger"] = "uint32_t";
            _typeMap["ULong"] = "uint64_t";
        }

        protected override void InitializeBinaryOperators()
        {
            // Arithmetic
            _binaryOpMap[BinaryOpKind.Add] = "+";
            _binaryOpMap[BinaryOpKind.Sub] = "-";
            _binaryOpMap[BinaryOpKind.Mul] = "*";
            _binaryOpMap[BinaryOpKind.Div] = "/";
            _binaryOpMap[BinaryOpKind.Mod] = "%";
            _binaryOpMap[BinaryOpKind.IntDiv] = "/";

            // Bitwise
            _binaryOpMap[BinaryOpKind.And] = "&";
            _binaryOpMap[BinaryOpKind.Or] = "|";
            _binaryOpMap[BinaryOpKind.Xor] = "^";
            _binaryOpMap[BinaryOpKind.Shl] = "<<";
            _binaryOpMap[BinaryOpKind.Shr] = ">>";

            // Comparison
            _binaryOpMap[BinaryOpKind.Eq] = "==";
            _binaryOpMap[BinaryOpKind.Ne] = "!=";
            _binaryOpMap[BinaryOpKind.Lt] = "<";
            _binaryOpMap[BinaryOpKind.Le] = "<=";
            _binaryOpMap[BinaryOpKind.Gt] = ">";
            _binaryOpMap[BinaryOpKind.Ge] = ">=";

            // String concatenation in C++ uses +
            _binaryOpMap[BinaryOpKind.Concat] = "+";
        }

        protected override void InitializeCompareOperators()
        {
            _compareOpMap[CompareKind.Eq] = "==";
            _compareOpMap[CompareKind.Ne] = "!=";
            _compareOpMap[CompareKind.Lt] = "<";
            _compareOpMap[CompareKind.Le] = "<=";
            _compareOpMap[CompareKind.Gt] = ">";
            _compareOpMap[CompareKind.Ge] = ">=";
        }

        protected override void InitializeUnaryOperators()
        {
            _unaryOpMap[UnaryOpKind.Neg] = "-";
            _unaryOpMap[UnaryOpKind.Not] = "!";
            _unaryOpMap[UnaryOpKind.BitwiseNot] = "~";
            _unaryOpMap[UnaryOpKind.Inc] = "++";
            _unaryOpMap[UnaryOpKind.Dec] = "--";
        }

        protected override string GetDefaultType() => "void*";

        protected override string MapArrayType(TypeInfo type)
        {
            var elementType = MapType(type.ElementType);
            return $"std::vector<{elementType}>";
        }

        protected override string MapPointerType(TypeInfo type)
        {
            var baseType = type.ElementType != null ? MapType(type.ElementType) : "void";
            return $"{baseType}*";
        }

        public override string GetDefaultValue(TypeInfo type)
        {
            if (type == null) return "nullptr";

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

        /// <summary>
        /// Get required includes for this type
        /// </summary>
        public IEnumerable<string> GetRequiredIncludes(TypeInfo type)
        {
            var includes = new List<string>();

            if (type == null) return includes;

            var typeName = type.Name?.ToLower() ?? "";

            if (typeName == "string")
                includes.Add("<string>");

            if (type.Kind == TypeKind.Array)
                includes.Add("<vector>");

            // Standard int types
            if (typeName is "integer" or "long" or "byte" or "short" or "ubyte" or "ushort" or "uinteger" or "ulong")
                includes.Add("<cstdint>");

            return includes;
        }
    }

    /// <summary>
    /// Type mapper for LLVM IR backend
    /// </summary>
    public class LLVMTypeMapper : TypeMapperBase
    {
        protected override void InitializeTypeMappings()
        {
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
            _typeMap["UByte"] = "i8";
            _typeMap["UShort"] = "i16";
            _typeMap["UInteger"] = "i32";
            _typeMap["ULong"] = "i64";
        }

        protected override void InitializeBinaryOperators()
        {
            // Arithmetic (integer versions - floating point uses fadd, fsub, etc.)
            _binaryOpMap[BinaryOpKind.Add] = "add";
            _binaryOpMap[BinaryOpKind.Sub] = "sub";
            _binaryOpMap[BinaryOpKind.Mul] = "mul";
            _binaryOpMap[BinaryOpKind.Div] = "sdiv";
            _binaryOpMap[BinaryOpKind.Mod] = "srem";
            _binaryOpMap[BinaryOpKind.IntDiv] = "sdiv";

            // Bitwise
            _binaryOpMap[BinaryOpKind.And] = "and";
            _binaryOpMap[BinaryOpKind.Or] = "or";
            _binaryOpMap[BinaryOpKind.Xor] = "xor";
            _binaryOpMap[BinaryOpKind.Shl] = "shl";
            _binaryOpMap[BinaryOpKind.Shr] = "ashr";
        }

        protected override void InitializeCompareOperators()
        {
            // Integer comparisons
            _compareOpMap[CompareKind.Eq] = "eq";
            _compareOpMap[CompareKind.Ne] = "ne";
            _compareOpMap[CompareKind.Lt] = "slt";
            _compareOpMap[CompareKind.Le] = "sle";
            _compareOpMap[CompareKind.Gt] = "sgt";
            _compareOpMap[CompareKind.Ge] = "sge";
        }

        protected override void InitializeUnaryOperators()
        {
            // LLVM doesn't have direct unary ops - they're expressed differently
            _unaryOpMap[UnaryOpKind.Neg] = "sub";     // 0 - x
            _unaryOpMap[UnaryOpKind.Not] = "xor";     // xor with true
            _unaryOpMap[UnaryOpKind.BitwiseNot] = "xor";  // xor with -1
        }

        protected override string GetDefaultType() => "i8*";

        protected override string MapArrayType(TypeInfo type)
        {
            var elementType = MapType(type.ElementType);
            return $"{elementType}*"; // Arrays are pointers in LLVM
        }

        protected override string MapPointerType(TypeInfo type)
        {
            var baseType = type.ElementType != null ? MapType(type.ElementType) : "i8";
            return $"{baseType}*";
        }

        public override string GetDefaultValue(TypeInfo type)
        {
            if (type == null) return "null";

            var typeName = type.Name?.ToLower() ?? "";

            return typeName switch
            {
                "integer" or "long" or "short" or "byte" => "0",
                "single" => "0.0",
                "double" => "0.0",
                "boolean" => "false",
                "char" => "0",
                "string" => "null",
                _ when type.Kind == TypeKind.Pointer => "null",
                _ => "zeroinitializer"
            };
        }

        /// <summary>
        /// Get floating-point version of binary operator
        /// </summary>
        public string MapFloatBinaryOperator(BinaryOpKind op)
        {
            return op switch
            {
                BinaryOpKind.Add => "fadd",
                BinaryOpKind.Sub => "fsub",
                BinaryOpKind.Mul => "fmul",
                BinaryOpKind.Div => "fdiv",
                BinaryOpKind.Mod => "frem",
                _ => MapBinaryOperator(op)
            };
        }

        /// <summary>
        /// Get floating-point version of comparison operator
        /// </summary>
        public string MapFloatComparisonOperator(CompareKind op)
        {
            return op switch
            {
                CompareKind.Eq => "oeq",
                CompareKind.Ne => "one",
                CompareKind.Lt => "olt",
                CompareKind.Le => "ole",
                CompareKind.Gt => "ogt",
                CompareKind.Ge => "oge",
                _ => MapComparisonOperator(op)
            };
        }
    }

    /// <summary>
    /// Type mapper for .NET IL (MSIL) backend
    /// </summary>
    public class MSILTypeMapper : TypeMapperBase
    {
        protected override void InitializeTypeMappings()
        {
            _typeMap["Integer"] = "int32";
            _typeMap["Long"] = "int64";
            _typeMap["Single"] = "float32";
            _typeMap["Double"] = "float64";
            _typeMap["String"] = "string";
            _typeMap["Boolean"] = "bool";
            _typeMap["Char"] = "char";
            _typeMap["Void"] = "void";
            _typeMap["Object"] = "object";
            _typeMap["Byte"] = "int8";
            _typeMap["Short"] = "int16";
            _typeMap["UByte"] = "uint8";
            _typeMap["UShort"] = "uint16";
            _typeMap["UInteger"] = "uint32";
            _typeMap["ULong"] = "uint64";
        }

        protected override void InitializeBinaryOperators()
        {
            // IL uses stack-based operations
            _binaryOpMap[BinaryOpKind.Add] = "add";
            _binaryOpMap[BinaryOpKind.Sub] = "sub";
            _binaryOpMap[BinaryOpKind.Mul] = "mul";
            _binaryOpMap[BinaryOpKind.Div] = "div";
            _binaryOpMap[BinaryOpKind.Mod] = "rem";
            _binaryOpMap[BinaryOpKind.IntDiv] = "div";

            // Bitwise
            _binaryOpMap[BinaryOpKind.And] = "and";
            _binaryOpMap[BinaryOpKind.Or] = "or";
            _binaryOpMap[BinaryOpKind.Xor] = "xor";
            _binaryOpMap[BinaryOpKind.Shl] = "shl";
            _binaryOpMap[BinaryOpKind.Shr] = "shr";
        }

        protected override void InitializeCompareOperators()
        {
            _compareOpMap[CompareKind.Eq] = "ceq";
            _compareOpMap[CompareKind.Ne] = "ceq"; // followed by not
            _compareOpMap[CompareKind.Lt] = "clt";
            _compareOpMap[CompareKind.Le] = "cgt"; // followed by not
            _compareOpMap[CompareKind.Gt] = "cgt";
            _compareOpMap[CompareKind.Ge] = "clt"; // followed by not
        }

        protected override void InitializeUnaryOperators()
        {
            _unaryOpMap[UnaryOpKind.Neg] = "neg";
            _unaryOpMap[UnaryOpKind.Not] = "not";
            _unaryOpMap[UnaryOpKind.BitwiseNot] = "not";
        }

        protected override string GetDefaultType() => "object";

        protected override string MapArrayType(TypeInfo type)
        {
            var elementType = MapType(type.ElementType);
            return $"{elementType}[]";
        }

        protected override string MapPointerType(TypeInfo type)
        {
            var baseType = type.ElementType != null ? MapType(type.ElementType) : "void";
            return $"{baseType}*";
        }

        public override string GetDefaultValue(TypeInfo type)
        {
            // IL doesn't have default value syntax - uses initobj or ldnull
            return "default";
        }
    }
}
