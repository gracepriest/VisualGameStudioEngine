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
                    var fieldType = "object"; // TODO: resolve via TypeMapper when field sig parsing is fixed
                    fields.Add(new ILFieldInfo(fieldName, fieldType));
                }

                // Methods
                var methods = new List<ILMethodInfo>();
                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var methodDef = _reader.GetMethodDefinition(methodHandle);
                    var methodName = _reader.GetString(methodDef.Name);
                    var sig = methodDef.DecodeSignature(new SimpleTypeProvider(_reader), default);
                    var returnType = sig.ReturnType;
                    var paramTypes = new List<string>();
                    foreach (var p in sig.ParameterTypes)
                    {
                        paramTypes.Add(p);
                    }
                    methods.Add(new ILMethodInfo(methodName, returnType, paramTypes));
                }

                result.Add(new ILTypeInfo(fullName, baseType, fields, methods,
                    typeDef.Attributes.HasFlag(System.Reflection.TypeAttributes.Interface),
                    baseType == "System.Enum"));
            }
            return result;
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