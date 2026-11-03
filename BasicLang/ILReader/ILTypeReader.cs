using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace BasicLang.Compiler.ILReader
{
    /// <summary>
    /// Reads type definitions from a .NET assembly.
    /// Phase 1: types, fields, methods, base types, generics.
    /// </summary>
    public class ILTypeReader
    {
        private readonly MetadataReader _reader;

        public ILTypeReader(MetadataReader reader)
        {
            _reader = reader;
        }

        public IReadOnlyList<ILTypeInfo> ReadTypes()
        {
            var result = new List<ILTypeInfo>();
            foreach (var handle in _reader.TypeDefinitions)
            {
                var typeDef = _reader.GetTypeDefinition(handle);
                var name = _reader.GetString(typeDef.Name);
                var ns = _reader.GetString(typeDef.Namespace);
                var fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

                // Base type
                string baseType = null;
                if (!typeDef.BaseType.IsNil)
                {
                    baseType = ResolveTypeName(typeDef.BaseType);
                }

                // Fields
                var fields = new List<ILFieldInfo>();
                foreach (var fieldHandle in typeDef.GetFields())
                {
                    var fieldDef = _reader.GetFieldDefinition(fieldHandle);
                    var fieldName = _reader.GetString(fieldDef.Name);
                    // Field type resolution via DecodeSignature varies by SDK version; resolve lazily.
                    // Resolve field type from the signature blob
                    var fieldType = ResolveFieldType(fieldDef);
                    fields.Add(new ILFieldInfo(fieldName, fieldType));
                }

                // Methods
                var methods = new List<ILMethodInfo>();
                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var methodDef = _reader.GetMethodDefinition(methodHandle);
                    var methodName = _reader.GetString(methodDef.Name);
                    var sig = methodDef.DecodeSignature(new SimpleTypeProvider(_reader), default);
                    var returnType = ILTypeMapper.Map(sig.ReturnType);
                    var paramTypes = new List<string>();
                    foreach (var p in sig.ParameterTypes)
                    {
                        paramTypes.Add(ILTypeMapper.Map(p));
                    }
                    methods.Add(new ILMethodInfo(methodName, returnType, paramTypes));
                }

                result.Add(new ILTypeInfo(fullName, baseType, fields, methods,
                    typeDef.Attributes.HasFlag(System.Reflection.TypeAttributes.Interface),
                    baseType == "System.Enum"));
            }
            return result;
        }

        private string ResolveFieldType(FieldDefinition fieldDef)
        {
            try
            {
                var sigBlob = fieldDef.Signature;
                if (sigBlob.IsNil) return "object";
                var reader = _reader.GetBlobReader(sigBlob);
                // Field signature: FIELD (0x06) + type
                if (reader.Length == 0) return "object";
                byte first = reader.ReadByte();
                // 0x06 = FIELD signature
                if (first != 0x06) return "object";
                // Decode the type using a simple type reader
                return ReadTypeFromBlob(ref reader);
            }
            catch
            {
                return "object";
            }
        }

        private string ReadTypeFromBlob(ref BlobReader reader)
        {
            if (reader.Length == 0) return "object";
            byte typeCode = reader.ReadByte();
            switch (typeCode)
            {
                case 0x02: return "bool";      // BOOLEAN
                case 0x03: return "char";       // CHAR
                case 0x04: return "sbyte";      // I1
                case 0x05: return "byte";       // U1
                case 0x06: return "short";      // I2
                case 0x07: return "ushort";     // U2
                case 0x08: return "int";        // I4
                case 0x09: return "uint";       // U4
                case 0x0A: return "long";       // I8
                case 0x0B: return "ulong";      // U8
                case 0x0C: return "float";      // R4
                case 0x0D: return "double";     // R8
                case 0x0E: return "string";     // STRING
                case 0x0F: return "object";     // PTR (simplified)
                case 0x10:                      // BYREF
                    return ReadTypeFromBlob(ref reader) + "&";
                case 0x11:                      // VALUETYPE
                    {
                        var token = ReadTypeDefOrRef(ref reader);
                        return ResolveTypeName(token);
                    }
                case 0x12:                      // CLASS
                    {
                        var token = ReadTypeDefOrRef(ref reader);
                        return ResolveTypeName(token);
                    }
                case 0x13:                      // VAR (generic type param)
                    {
                        int index = ReadCompressedUInt(ref reader);
                        return $"!{index}";
                    }
                case 0x14:                      // ARRAY (SZARRAY)
                    {
                        var elemType = ReadTypeFromBlob(ref reader);
                        return elemType + "[]";
                    }
                case 0x15:                      // GENERICINST
                    {
                        byte gType = reader.ReadByte(); // should be 0x12 (CLASS) or 0x11 (VALUETYPE)
                        var genTypeToken = ReadTypeDefOrRef(ref reader);
                        var genTypeName = ResolveTypeName(genTypeToken);
                        int argCount = ReadCompressedUInt(ref reader);
                        var args = new List<string>();
                        for (int i = 0; i < argCount; i++)
                            args.Add(ReadTypeFromBlob(ref reader));
                        return $"{genTypeName}<{string.Join(",", args)}>";
                    }
                default:
                    return "object";
            }
        }

        private EntityHandle ReadTypeDefOrRef(ref BlobReader reader)
        {
            uint token = (uint)ReadCompressedUInt(ref reader);
            // The token is encoded as (table << 24) | rid, but compressed
            // Actually, in signature blobs, TypeDefOrRef is encoded as a compressed token
            // where the low 2 bits indicate the table (0=TypeDef, 1=TypeRef, 2=TypeSpec)
            int table = (int)(token & 0x03);
            int rid = (int)(token >> 2);
            switch (table)
            {
                case 0: return MetadataTokens.TypeDefinitionHandle(rid);
                case 1: return MetadataTokens.TypeReferenceHandle(rid);
                case 2: return MetadataTokens.TypeSpecificationHandle(rid);
                default: return default;
            }
        }

        private int ReadCompressedUInt(ref BlobReader reader)
        {
            byte first = reader.ReadByte();
            if ((first & 0x80) == 0) return first;
            if ((first & 0xC0) == 0x80)
            {
                byte second = reader.ReadByte();
                return ((first & 0x3F) << 8) | second;
            }
            // 4-byte form
            byte b2 = reader.ReadByte();
            byte b3 = reader.ReadByte();
            byte b4 = reader.ReadByte();
            return ((first & 0x1F) << 24) | (b2 << 16) | (b3 << 8) | b4;
        }

        public string ResolveTypeName(EntityHandle handle)
        {
            if (handle.IsNil) return "void";
            if (handle.Kind == HandleKind.TypeDefinition)
            {
                var td = _reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                var n = _reader.GetString(td.Name);
                var ns = _reader.GetString(td.Namespace);
                return string.IsNullOrEmpty(ns) ? n : $"{ns}.{n}";
            }
            if (handle.Kind == HandleKind.TypeReference)
            {
                var tr = _reader.GetTypeReference((TypeReferenceHandle)handle);
                var n = _reader.GetString(tr.Name);
                var ns = _reader.GetString(tr.Namespace);
                return string.IsNullOrEmpty(ns) ? n : $"{ns}.{n}";
            }
            if (handle.Kind == HandleKind.TypeSpecification)
            {
                var ts = _reader.GetTypeSpecification((TypeSpecificationHandle)handle);
                var sig = ts.DecodeSignature(new SimpleTypeProvider(_reader), default);
                return sig;
            }
            return handle.Kind.ToString();
        }

    }

    public class ILTypeInfo
    {
        public string FullName { get; }
        public string BaseType { get; }
        public IReadOnlyList<ILFieldInfo> Fields { get; }
        public IReadOnlyList<ILMethodInfo> Methods { get; }
        public bool IsInterface { get; }
        public bool IsEnum { get; }

        public ILTypeInfo(string fullName, string baseType, IReadOnlyList<ILFieldInfo> fields,
            IReadOnlyList<ILMethodInfo> methods, bool isInterface, bool isEnum)
        {
            FullName = fullName;
            BaseType = baseType;
            Fields = fields;
            Methods = methods;
            IsInterface = isInterface;
            IsEnum = isEnum;
        }
    }

    public class ILFieldInfo
    {
        public string Name { get; }
        public string Type { get; }
        public ILFieldInfo(string name, string type) { Name = name; Type = type; }
    }

    public class ILMethodInfo
    {
        public string Name { get; }
        public string ReturnType { get; }
        public IReadOnlyList<string> ParameterTypes { get; }
        public ILMethodInfo(string name, string returnType, IReadOnlyList<string> paramTypes)
        {
            Name = name; ReturnType = returnType; ParameterTypes = paramTypes;
        }
    }

    /// <summary>
    /// Simple type provider for DecodeSignature. Maps type tokens to strings.
    /// </summary>
    internal class SimpleTypeProvider : ISignatureTypeProvider<string, object>
    {
        private readonly MetadataReader _reader;
        public SimpleTypeProvider(MetadataReader reader) { _reader = reader; }

        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments)
        {
            var args = string.Join(",", typeArguments);
            return $"{genericType}<{args}>";
        }
        public string GetGenericMethodParameter(object genericContext, int index) => $"!!{index}";
        public string GetGenericTypeParameter(object genericContext, int index) => $"!{index}";
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            switch (typeCode)
            {
                case PrimitiveTypeCode.Boolean: return "bool";
                case PrimitiveTypeCode.Char: return "char";
                case PrimitiveTypeCode.SByte: return "sbyte";
                case PrimitiveTypeCode.Byte: return "byte";
                case PrimitiveTypeCode.Int16: return "short";
                case PrimitiveTypeCode.UInt16: return "ushort";
                case PrimitiveTypeCode.Int32: return "int";
                case PrimitiveTypeCode.UInt32: return "uint";
                case PrimitiveTypeCode.Int64: return "long";
                case PrimitiveTypeCode.UInt64: return "ulong";
                case PrimitiveTypeCode.Single: return "float";
                case PrimitiveTypeCode.Double: return "double";
                case PrimitiveTypeCode.String: return "string";
                case PrimitiveTypeCode.Object: return "object";
                case PrimitiveTypeCode.Void: return "void";
                default: return typeCode.ToString();
            }
        }
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var td = reader.GetTypeDefinition(handle);
            var n = reader.GetString(td.Name);
            var ns = reader.GetString(td.Namespace);
            return string.IsNullOrEmpty(ns) ? n : $"{ns}.{n}";
        }
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var tr = reader.GetTypeReference(handle);
            var n = reader.GetString(tr.Name);
            var ns = reader.GetString(tr.Namespace);
            return string.IsNullOrEmpty(ns) ? n : $"{ns}.{n}";
        }
        public string GetTypeFromSpecification(MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            var ts = reader.GetTypeSpecification(handle);
            var sig = ts.DecodeSignature(this, genericContext);
            return sig;
        }

        public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate";

        public string GetSZArrayType(string elementType) => elementType + "[]";
    }
}