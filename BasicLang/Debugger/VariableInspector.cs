using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BasicLang.Debugger
{
    /// <summary>
    /// Reads ICorDebugValue hierarchy and converts to DAP variable format.
    /// Maintains a reference map so expandable values (objects, arrays) can have
    /// their children fetched on demand via a stable variablesReference ID.
    /// </summary>
    public class VariableInspector
    {
        private readonly Dictionary<int, object> _variableReferences = new(); // DAP ref ID → ICorDebugValue
        private int _nextRefId = 1;

        // -------------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------------

        /// <summary>
        /// Inspect a CLR value and return a DAP variable representation.
        /// </summary>
        public DapVariable InspectValue(object corDebugValue, string name)
        {
            if (corDebugValue == null)
                return MakeVariable(name, "Nothing", "Object", 0);

            try
            {
                // Unwrap reference values first (handles null references)
                if (corDebugValue is ICorDebugReferenceValue refVal)
                    return InspectReferenceValue(refVal, name);

                // Unwrap boxed value types
                if (corDebugValue is ICorDebugBoxedValue boxedVal)
                    return InspectBoxedValue(boxedVal, name);

                // Primitive/struct value types — read raw bytes
                if (corDebugValue is ICorDebugGenericValue genericVal)
                    return InspectGenericValue(genericVal, name);

                // Strings
                if (corDebugValue is ICorDebugStringValue stringVal)
                    return InspectStringValue(stringVal, name);

                // Arrays
                if (corDebugValue is ICorDebugArrayValue arrayVal)
                    return InspectArrayValue(arrayVal, name);

                // Objects (class instances, structs via ICorDebugObjectValue)
                if (corDebugValue is ICorDebugObjectValue objectVal)
                    return InspectObjectValue(objectVal, name);

                // Fallback — try ICorDebugValue for type info only
                if (corDebugValue is ICorDebugValue val)
                {
                    val.GetType(out CorElementType elemType);
                    return MakeVariable(name, $"<{elemType}>", elemType.ToString(), 0);
                }

                return MakeVariable(name, "<unknown>", "Object", 0);
            }
            catch (Exception ex)
            {
                return MakeVariable(name, $"<error: {ex.Message}>", "Error", 0);
            }
        }

        /// <summary>
        /// Get child variables for an expandable reference (object fields or array elements).
        /// </summary>
        public List<DapVariable> GetChildren(int variablesReference)
        {
            var result = new List<DapVariable>();

            if (!_variableReferences.TryGetValue(variablesReference, out object storedValue))
                return result;

            try
            {
                if (storedValue is ICorDebugObjectValue objectVal)
                    return GetObjectFields(objectVal);

                if (storedValue is ICorDebugArrayValue arrayVal)
                    return GetArrayElements(arrayVal);
            }
            catch (Exception ex)
            {
                result.Add(MakeVariable("<error>", ex.Message, "Error", 0));
            }

            return result;
        }

        /// <summary>
        /// Get local variables from a stack frame.
        /// </summary>
        public List<DapVariable> GetLocals(object corDebugILFrame)
        {
            var result = new List<DapVariable>();

            if (corDebugILFrame is not ICorDebugILFrame ilFrame)
                return result;

            try
            {
                int hr = ilFrame.EnumerateLocalVariables(out ICorDebugValueEnum valueEnum);
                if (hr < 0 || valueEnum == null)
                    return result;

                var values = EnumerateValues(valueEnum);
                for (int i = 0; i < values.Count; i++)
                {
                    try
                    {
                        var dapVar = InspectValue(values[i], $"local_{i}");
                        result.Add(dapVar);
                    }
                    catch
                    {
                        result.Add(MakeVariable($"local_{i}", "<unavailable>", "Object", 0));
                    }
                }
            }
            catch (Exception ex)
            {
                result.Add(MakeVariable("<error>", ex.Message, "Error", 0));
            }

            return result;
        }

        /// <summary>
        /// Get arguments from a stack frame.
        /// </summary>
        public List<DapVariable> GetArguments(object corDebugILFrame)
        {
            var result = new List<DapVariable>();

            if (corDebugILFrame is not ICorDebugILFrame ilFrame)
                return result;

            try
            {
                int hr = ilFrame.EnumerateArguments(out ICorDebugValueEnum valueEnum);
                if (hr < 0 || valueEnum == null)
                    return result;

                var values = EnumerateValues(valueEnum);
                for (int i = 0; i < values.Count; i++)
                {
                    try
                    {
                        var dapVar = InspectValue(values[i], $"arg_{i}");
                        result.Add(dapVar);
                    }
                    catch
                    {
                        result.Add(MakeVariable($"arg_{i}", "<unavailable>", "Object", 0));
                    }
                }
            }
            catch (Exception ex)
            {
                result.Add(MakeVariable("<error>", ex.Message, "Error", 0));
            }

            return result;
        }

        /// <summary>
        /// Clear all cached variable references (call on continue/step).
        /// </summary>
        public void ClearReferences()
        {
            _variableReferences.Clear();
            _nextRefId = 1;
        }

        // -------------------------------------------------------------------------
        // Private inspection helpers
        // -------------------------------------------------------------------------

        private DapVariable InspectReferenceValue(ICorDebugReferenceValue refVal, string name)
        {
            try
            {
                int hr = refVal.IsNull(out bool isNull);
                if (hr < 0 || isNull)
                    return MakeVariable(name, "Nothing", "Object", 0);

                hr = refVal.Dereference(out ICorDebugValue derefed);
                if (hr < 0 || derefed == null)
                    return MakeVariable(name, "Nothing", "Object", 0);

                return InspectValue(derefed, name);
            }
            catch
            {
                return MakeVariable(name, "Nothing", "Object", 0);
            }
        }

        private DapVariable InspectBoxedValue(ICorDebugBoxedValue boxedVal, string name)
        {
            try
            {
                int hr = boxedVal.GetObject(out ICorDebugObjectValue inner);
                if (hr < 0 || inner == null)
                    return MakeVariable(name, "<boxed>", "Object", 0);

                return InspectObjectValue(inner, name);
            }
            catch
            {
                return MakeVariable(name, "<boxed>", "Object", 0);
            }
        }

        private DapVariable InspectGenericValue(ICorDebugGenericValue genericVal, string name)
        {
            try
            {
                genericVal.GetType(out CorElementType elemType);
                genericVal.GetSize(out uint size);

                if (size == 0)
                    return MakeVariable(name, "<empty>", elemType.ToString(), 0);

                // Allocate unmanaged memory, read raw bytes, then free
                IntPtr pBuffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    int hr = genericVal.GetValue(pBuffer);
                    if (hr < 0)
                        return MakeVariable(name, "<unreadable>", elemType.ToString(), 0);

                    byte[] bytes = new byte[size];
                    Marshal.Copy(pBuffer, bytes, 0, (int)size);

                    string displayValue = ReadPrimitiveValue(elemType, bytes);
                    string typeName = CorElementTypeToTypeName(elemType);
                    return MakeVariable(name, displayValue, typeName, 0);
                }
                finally
                {
                    Marshal.FreeHGlobal(pBuffer);
                }
            }
            catch (Exception ex)
            {
                return MakeVariable(name, $"<error: {ex.Message}>", "Error", 0);
            }
        }

        private DapVariable InspectStringValue(ICorDebugStringValue stringVal, string name)
        {
            try
            {
                int hr = stringVal.GetLength(out uint length);
                if (hr < 0)
                    return MakeVariable(name, "<unreadable string>", "String", 0);

                if (length == 0)
                    return MakeVariable(name, "\"\"", "String", 0);

                char[] buffer = new char[length + 1];
                hr = stringVal.GetString(length + 1, out uint fetched, buffer);
                if (hr < 0)
                    return MakeVariable(name, "<unreadable string>", "String", 0);

                string value = new string(buffer, 0, (int)fetched);
                string escaped = EscapeString(value);
                // Truncate long strings to ~100 chars for preview
                string preview = escaped.Length > 100
                    ? escaped.Substring(0, 100) + "..."
                    : escaped;
                return MakeVariable(name, $"\"{preview}\"", "String", 0);
            }
            catch (Exception ex)
            {
                return MakeVariable(name, $"<error: {ex.Message}>", "String", 0);
            }
        }

        private DapVariable InspectArrayValue(ICorDebugArrayValue arrayVal, string name)
        {
            try
            {
                arrayVal.GetCount(out uint count);
                arrayVal.GetElementType(out CorElementType elemType);
                string elemTypeName = CorElementTypeToTypeName(elemType);
                // BasicLang-style array type: Integer(5), String(3), etc.
                string typeName = $"{elemTypeName}({count})";

                if (count == 0)
                    return MakeVariable(name, $"{elemTypeName}(0)", typeName, 0);

                // Register for child fetch
                int refId = RegisterReference(arrayVal);
                return MakeVariable(name, $"{elemTypeName}({count})", typeName, refId);
            }
            catch (Exception ex)
            {
                return MakeVariable(name, $"<error: {ex.Message}>", "Array", 0);
            }
        }

        private DapVariable InspectObjectValue(ICorDebugObjectValue objectVal, string name)
        {
            try
            {
                objectVal.GetType(out CorElementType elemType);
                string typeName = elemType == CorElementType.ELEMENT_TYPE_CLASS ? "Object" : "Struct";

                // Try to get the class name via ICorDebugClass → module token
                try
                {
                    objectVal.GetClass(out ICorDebugClass cls);
                    if (cls != null)
                    {
                        cls.GetModule(out ICorDebugModule module);
                        cls.GetToken(out uint typeDef);
                        if (module != null)
                            typeName = ResolveTypeName(module, typeDef) ?? typeName;
                    }
                }
                catch
                {
                    // Class name resolution is best-effort
                }

                // Map CLR type names to BasicLang equivalents
                typeName = MapToBasicLangTypeName(typeName);

                int refId = RegisterReference(objectVal);
                return MakeVariable(name, $"{{{typeName}}}", typeName, refId);
            }
            catch (Exception ex)
            {
                return MakeVariable(name, $"<error: {ex.Message}>", "Object", 0);
            }
        }

        // -------------------------------------------------------------------------
        // Children: object fields
        // -------------------------------------------------------------------------

        private List<DapVariable> GetObjectFields(ICorDebugObjectValue objectVal)
        {
            var result = new List<DapVariable>();

            try
            {
                objectVal.GetClass(out ICorDebugClass cls);
                if (cls == null)
                    return result;

                cls.GetModule(out ICorDebugModule module);
                cls.GetToken(out uint typeDef);

                if (module == null)
                    return result;

                // Use IMetaDataImport to enumerate fields
                Guid imdImportGuid = new Guid("7DAC8207-D3AE-4C75-9B67-92801A497D44");
                module.GetMetaDataInterface(ref imdImportGuid, out object mdObj);

                if (mdObj is not IMetaDataImport mdImport)
                    return result;

                // Enumerate fields for this type
                IntPtr hEnum = IntPtr.Zero;
                try
                {
                    uint[] fieldTokens = new uint[32];
                    while (true)
                    {
                        int hr = mdImport.EnumFields(ref hEnum, typeDef, fieldTokens, (uint)fieldTokens.Length, out uint fetched);
                        if (hr < 0 || fetched == 0)
                            break;

                        for (uint i = 0; i < fetched; i++)
                        {
                            uint fieldToken = fieldTokens[i];
                            string fieldName = GetFieldName(mdImport, fieldToken);

                            try
                            {
                                objectVal.GetFieldValue(cls, fieldToken, out ICorDebugValue fieldValue);
                                var dapVar = InspectValue(fieldValue, fieldName);
                                result.Add(dapVar);
                            }
                            catch
                            {
                                result.Add(MakeVariable(fieldName, "<unavailable>", "Object", 0));
                            }
                        }

                        if (fetched < (uint)fieldTokens.Length)
                            break;
                    }
                }
                finally
                {
                    if (hEnum != IntPtr.Zero)
                    {
                        try { mdImport.CloseEnum(hEnum); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Add(MakeVariable("<error>", ex.Message, "Error", 0));
            }

            return result;
        }

        // -------------------------------------------------------------------------
        // Children: array elements
        // -------------------------------------------------------------------------

        private List<DapVariable> GetArrayElements(ICorDebugArrayValue arrayVal)
        {
            var result = new List<DapVariable>();

            try
            {
                arrayVal.GetCount(out uint count);
                // Cap display to a reasonable limit to avoid flooding the UI
                uint limit = Math.Min(count, 100);

                for (uint i = 0; i < limit; i++)
                {
                    try
                    {
                        uint[] indices = new uint[] { i };
                        arrayVal.GetElement(1, indices, out ICorDebugValue element);
                        var dapVar = InspectValue(element, $"[{i}]");
                        result.Add(dapVar);
                    }
                    catch
                    {
                        result.Add(MakeVariable($"[{i}]", "<unavailable>", "Object", 0));
                    }
                }

                if (count > limit)
                    result.Add(MakeVariable("...", $"({count - limit} more elements)", "", 0));
            }
            catch (Exception ex)
            {
                result.Add(MakeVariable("<error>", ex.Message, "Error", 0));
            }

            return result;
        }

        // -------------------------------------------------------------------------
        // Enumeration helper
        // -------------------------------------------------------------------------

        private static List<ICorDebugValue> EnumerateValues(ICorDebugValueEnum valueEnum)
        {
            var result = new List<ICorDebugValue>();
            if (valueEnum == null)
                return result;

            valueEnum.GetCount(out uint total);
            if (total == 0)
                return result;

            uint batchSize = Math.Min(total, 64);
            ICorDebugValue[] batch = new ICorDebugValue[batchSize];

            while (true)
            {
                int hr = valueEnum.Next(batchSize, batch, out uint fetched);
                if (hr < 0 || fetched == 0)
                    break;

                for (uint i = 0; i < fetched; i++)
                {
                    if (batch[i] != null)
                        result.Add(batch[i]);
                }

                if (fetched < batchSize)
                    break;
            }

            return result;
        }

        // -------------------------------------------------------------------------
        // Primitive value reading
        // -------------------------------------------------------------------------

        private static string ReadPrimitiveValue(CorElementType elemType, byte[] bytes)
        {
            try
            {
                return elemType switch
                {
                    CorElementType.ELEMENT_TYPE_BOOLEAN =>
                        (bytes.Length > 0 && bytes[0] != 0) ? "True" : "False",

                    CorElementType.ELEMENT_TYPE_CHAR =>
                        bytes.Length >= 2
                            ? $"'{(char)BitConverter.ToUInt16(bytes, 0)}'"
                            : $"'\\u{bytes[0]:X4}'",

                    CorElementType.ELEMENT_TYPE_I1 =>
                        bytes.Length >= 1 ? ((sbyte)bytes[0]).ToString() : "0",

                    CorElementType.ELEMENT_TYPE_U1 =>
                        bytes.Length >= 1 ? bytes[0].ToString() : "0",

                    CorElementType.ELEMENT_TYPE_I2 =>
                        bytes.Length >= 2 ? BitConverter.ToInt16(bytes, 0).ToString() : "0",

                    CorElementType.ELEMENT_TYPE_U2 =>
                        bytes.Length >= 2 ? BitConverter.ToUInt16(bytes, 0).ToString() : "0",

                    CorElementType.ELEMENT_TYPE_I4 =>
                        bytes.Length >= 4 ? BitConverter.ToInt32(bytes, 0).ToString() : "0",

                    CorElementType.ELEMENT_TYPE_U4 =>
                        bytes.Length >= 4 ? BitConverter.ToUInt32(bytes, 0).ToString() : "0",

                    CorElementType.ELEMENT_TYPE_I8 =>
                        bytes.Length >= 8 ? BitConverter.ToInt64(bytes, 0).ToString() : "0",

                    CorElementType.ELEMENT_TYPE_U8 =>
                        bytes.Length >= 8 ? BitConverter.ToUInt64(bytes, 0).ToString() : "0",

                    CorElementType.ELEMENT_TYPE_R4 =>
                        bytes.Length >= 4 ? BitConverter.ToSingle(bytes, 0).ToString("G") : "0",

                    CorElementType.ELEMENT_TYPE_R8 =>
                        bytes.Length >= 8 ? BitConverter.ToDouble(bytes, 0).ToString("G") : "0",

                    CorElementType.ELEMENT_TYPE_I =>
                        bytes.Length >= 8
                            ? BitConverter.ToInt64(bytes, 0).ToString()
                            : bytes.Length >= 4
                                ? BitConverter.ToInt32(bytes, 0).ToString()
                                : "0",

                    CorElementType.ELEMENT_TYPE_U =>
                        bytes.Length >= 8
                            ? BitConverter.ToUInt64(bytes, 0).ToString()
                            : bytes.Length >= 4
                                ? BitConverter.ToUInt32(bytes, 0).ToString()
                                : "0",

                    _ => $"0x{BitConverter.ToString(bytes).Replace("-", "")}"
                };
            }
            catch
            {
                return $"0x{BitConverter.ToString(bytes).Replace("-", "")}";
            }
        }

        private static string CorElementTypeToTypeName(CorElementType elemType) => elemType switch
        {
            CorElementType.ELEMENT_TYPE_BOOLEAN   => "Boolean",
            CorElementType.ELEMENT_TYPE_CHAR      => "Char",
            CorElementType.ELEMENT_TYPE_I1        => "SByte",
            CorElementType.ELEMENT_TYPE_U1        => "Byte",
            CorElementType.ELEMENT_TYPE_I2        => "Short",
            CorElementType.ELEMENT_TYPE_U2        => "UShort",
            CorElementType.ELEMENT_TYPE_I4        => "Integer",
            CorElementType.ELEMENT_TYPE_U4        => "UInteger",
            CorElementType.ELEMENT_TYPE_I8        => "Long",
            CorElementType.ELEMENT_TYPE_U8        => "ULong",
            CorElementType.ELEMENT_TYPE_R4        => "Single",
            CorElementType.ELEMENT_TYPE_R8        => "Double",
            CorElementType.ELEMENT_TYPE_I         => "IntPtr",
            CorElementType.ELEMENT_TYPE_U         => "UIntPtr",
            CorElementType.ELEMENT_TYPE_STRING    => "String",
            CorElementType.ELEMENT_TYPE_CLASS     => "Object",
            CorElementType.ELEMENT_TYPE_VALUETYPE => "Struct",
            CorElementType.ELEMENT_TYPE_OBJECT    => "Object",
            _                                     => elemType.ToString()
        };

        /// <summary>
        /// Maps a CLR type name (from metadata) to the corresponding BasicLang type name.
        /// Falls back to the original name if no mapping exists.
        /// </summary>
        private static string MapToBasicLangTypeName(string clrTypeName)
        {
            if (string.IsNullOrEmpty(clrTypeName))
                return "Object";

            return clrTypeName switch
            {
                "Int32" => "Integer",
                "Int16" => "Short",
                "Int64" => "Long",
                "UInt32" => "UInteger",
                "UInt16" => "UShort",
                "UInt64" => "ULong",
                "Single" => "Single",
                "Double" => "Double",
                "Boolean" => "Boolean",
                "String" => "String",
                "Char" => "Char",
                "Byte" => "Byte",
                "SByte" => "SByte",
                "Decimal" => "Decimal",
                "Object" => "Object",
                "Void" => "Void",
                "System.Int32" => "Integer",
                "System.Int16" => "Short",
                "System.Int64" => "Long",
                "System.UInt32" => "UInteger",
                "System.UInt16" => "UShort",
                "System.UInt64" => "ULong",
                "System.Single" => "Single",
                "System.Double" => "Double",
                "System.Boolean" => "Boolean",
                "System.String" => "String",
                "System.Char" => "Char",
                "System.Byte" => "Byte",
                "System.SByte" => "SByte",
                "System.Decimal" => "Decimal",
                "System.Object" => "Object",
                "System.Void" => "Void",
                _ => clrTypeName
            };
        }

        // -------------------------------------------------------------------------
        // Metadata helpers
        // -------------------------------------------------------------------------

        private static string ResolveTypeName(ICorDebugModule module, uint typeDef)
        {
            try
            {
                Guid imdImportGuid = new Guid("7DAC8207-D3AE-4C75-9B67-92801A497D44");
                module.GetMetaDataInterface(ref imdImportGuid, out object mdObj);

                if (mdObj is not IMetaDataImport mdImport)
                    return null;

                char[] nameBuffer = new char[1024];
                int hr = mdImport.GetTypeDefProps(
                    typeDef,
                    nameBuffer,
                    (uint)nameBuffer.Length,
                    out uint nameLen,
                    out uint typeDefFlags,
                    out uint extends);

                if (hr < 0 || nameLen == 0)
                    return null;

                string fullName = new string(nameBuffer, 0, (int)(nameLen - 1));
                // Return only the simple name (after last '.')
                int dot = fullName.LastIndexOf('.');
                return dot >= 0 ? fullName.Substring(dot + 1) : fullName;
            }
            catch
            {
                return null;
            }
        }

        private static string GetFieldName(IMetaDataImport mdImport, uint fieldToken)
        {
            try
            {
                char[] nameBuffer = new char[512];
                int hr = mdImport.GetFieldProps(
                    fieldToken,
                    out uint classToken,
                    nameBuffer,
                    (uint)nameBuffer.Length,
                    out uint nameLen,
                    out uint fieldAttr,
                    out IntPtr pvSigBlob,
                    out uint cbSigBlob,
                    out uint dwCPlusTypeFlag,
                    out IntPtr ppValue,
                    out uint pcchValue);

                if (hr < 0 || nameLen == 0)
                    return $"field_0x{fieldToken:X}";

                return new string(nameBuffer, 0, (int)(nameLen - 1));
            }
            catch
            {
                return $"field_0x{fieldToken:X}";
            }
        }

        // -------------------------------------------------------------------------
        // Reference registration
        // -------------------------------------------------------------------------

        private int RegisterReference(object value)
        {
            int refId = _nextRefId++;
            _variableReferences[refId] = value;
            return refId;
        }

        // -------------------------------------------------------------------------
        // Utility
        // -------------------------------------------------------------------------

        private static DapVariable MakeVariable(string name, string value, string type, int variablesReference) =>
            new DapVariable
            {
                Name = name,
                Value = value,
                Type = type,
                VariablesReference = variablesReference
            };

        private static string EscapeString(string s) =>
            s.Replace("\\", "\\\\")
             .Replace("\"", "\\\"")
             .Replace("\n", "\\n")
             .Replace("\r", "\\r")
             .Replace("\t", "\\t");
    }

    // =========================================================================
    // IMetaDataImport — minimal COM interface for reading type/field metadata
    // =========================================================================

    [ComImport]
    [Guid("7DAC8207-D3AE-4C75-9B67-92801A497D44")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMetaDataImport
    {
        // vtable slot 0 — CloseEnum
        [PreserveSig]
        void CloseEnum(IntPtr hEnum);

        // vtable slot 1 — CountEnum
        [PreserveSig]
        int CountEnum(IntPtr hEnum, out uint pulCount);

        // vtable slot 2 — ResetEnum
        [PreserveSig]
        int ResetEnum(IntPtr hEnum, uint ulPos);

        // vtable slot 3 — EnumTypeDefs
        [PreserveSig]
        int EnumTypeDefs(
            ref IntPtr phEnum,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] uint[] rTypeDefs,
            uint cMax,
            out uint pcTypeDefs);

        // vtable slot 4 — EnumInterfaceImpls
        [PreserveSig]
        int EnumInterfaceImpls(
            ref IntPtr phEnum,
            uint td,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] uint[] rImpls,
            uint cMax,
            out uint pcImpls);

        // vtable slot 5 — EnumTypeRefs
        [PreserveSig]
        int EnumTypeRefs(
            ref IntPtr phEnum,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] uint[] rTypeRefs,
            uint cMax,
            out uint pcTypeRefs);

        // vtable slot 6 — FindTypeDefByName
        [PreserveSig]
        int FindTypeDefByName(
            [MarshalAs(UnmanagedType.LPWStr)] string szTypeDef,
            uint tkEnclosingClass,
            out uint ptd);

        // vtable slot 7 — GetScopeProps
        [PreserveSig]
        int GetScopeProps(
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] char[] szName,
            uint cchName,
            out uint pchName,
            out Guid pmvid);

        // vtable slot 8 — GetModuleFromScope
        [PreserveSig]
        int GetModuleFromScope(out uint pmd);

        // vtable slot 9 — GetTypeDefProps
        [PreserveSig]
        int GetTypeDefProps(
            uint td,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] char[] szTypeDef,
            uint cchTypeDef,
            out uint pchTypeDef,
            out uint pdwTypeDefFlags,
            out uint ptkExtends);

        // vtable slot 10 — GetInterfaceImplProps (placeholder)
        [PreserveSig]
        int GetInterfaceImplProps(uint iiImpl, out uint pClass, out uint ptkIface);

        // vtable slot 11 — GetTypeRefProps (placeholder)
        [PreserveSig]
        int GetTypeRefProps(
            uint tr,
            out uint ptkResolutionScope,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[] szName,
            uint cchName,
            out uint pchName);

        // vtable slot 12 — ResolveTypeRef (placeholder)
        [PreserveSig]
        int ResolveTypeRef(uint tr, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppIScope, out uint ptd);

        // vtable slot 13 — EnumMembers
        [PreserveSig]
        int EnumMembers(
            ref IntPtr phEnum,
            uint cl,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] uint[] rMembers,
            uint cMax,
            out uint pcTokens);

        // vtable slot 14 — EnumMembersWithName
        [PreserveSig]
        int EnumMembersWithName(
            ref IntPtr phEnum,
            uint cl,
            [MarshalAs(UnmanagedType.LPWStr)] string szName,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] uint[] rMembers,
            uint cMax,
            out uint pcTokens);

        // vtable slot 15 — EnumMethods
        [PreserveSig]
        int EnumMethods(
            ref IntPtr phEnum,
            uint cl,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] uint[] rMethods,
            uint cMax,
            out uint pcTokens);

        // vtable slot 16 — EnumMethodsWithName
        [PreserveSig]
        int EnumMethodsWithName(
            ref IntPtr phEnum,
            uint cl,
            [MarshalAs(UnmanagedType.LPWStr)] string szName,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] uint[] rMethods,
            uint cMax,
            out uint pcTokens);

        // vtable slot 17 — EnumFields
        [PreserveSig]
        int EnumFields(
            ref IntPtr phEnum,
            uint cl,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] uint[] rFields,
            uint cMax,
            out uint pcTokens);

        // vtable slot 18 — EnumFieldsWithName (placeholder)
        [PreserveSig]
        int EnumFieldsWithName(
            ref IntPtr phEnum,
            uint cl,
            [MarshalAs(UnmanagedType.LPWStr)] string szName,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] uint[] rFields,
            uint cMax,
            out uint pcTokens);

        // vtable slot 19 — EnumParams (placeholder)
        [PreserveSig]
        int EnumParams(
            ref IntPtr phEnum,
            uint mb,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] uint[] rParams,
            uint cMax,
            out uint pcTokens);

        // vtable slot 20 — EnumMemberRefs (placeholder)
        [PreserveSig]
        int EnumMemberRefs(
            ref IntPtr phEnum,
            uint tkParent,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] uint[] rMemberRefs,
            uint cMax,
            out uint pcTokens);

        // vtable slot 21 — EnumMethodImpls (placeholder)
        [PreserveSig]
        int EnumMethodImpls(
            ref IntPtr phEnum,
            uint td,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] uint[] rMethodBody,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] uint[] rMethodDecl,
            uint cMax,
            out uint pcTokens);

        // vtable slot 22 — EnumPermissionSets (placeholder)
        [PreserveSig]
        int EnumPermissionSets(
            ref IntPtr phEnum,
            uint tk,
            uint dwActions,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] uint[] rPermission,
            uint cMax,
            out uint pcTokens);

        // vtable slot 23 — FindMember (placeholder)
        [PreserveSig]
        int FindMember(uint td, [MarshalAs(UnmanagedType.LPWStr)] string szName, IntPtr pvSigBlob, uint cbSigBlob, out uint pmb);

        // vtable slot 24 — FindMethod (placeholder)
        [PreserveSig]
        int FindMethod(uint td, [MarshalAs(UnmanagedType.LPWStr)] string szName, IntPtr pvSigBlob, uint cbSigBlob, out uint pmb);

        // vtable slot 25 — FindField (placeholder)
        [PreserveSig]
        int FindField(uint td, [MarshalAs(UnmanagedType.LPWStr)] string szName, IntPtr pvSigBlob, uint cbSigBlob, out uint pmb);

        // vtable slot 26 — FindMemberRef (placeholder)
        [PreserveSig]
        int FindMemberRef(uint td, [MarshalAs(UnmanagedType.LPWStr)] string szName, IntPtr pvSigBlob, uint cbSigBlob, out uint pmr);

        // vtable slot 27 — GetMethodProps (placeholder)
        [PreserveSig]
        int GetMethodProps(
            uint mb,
            out uint pClass,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[] szMethod,
            uint cchMethod,
            out uint pchMethod,
            out uint pdwAttr,
            out IntPtr ppvSigBlob,
            out uint pcbSigBlob,
            out uint pulCodeRVA,
            out uint pdwImplFlags);

        // vtable slot 28 — GetMemberRefProps (placeholder)
        [PreserveSig]
        int GetMemberRefProps(
            uint mr,
            out uint ptk,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[] szName,
            uint cchName,
            out uint pchName,
            out IntPtr ppvSigBlob,
            out uint pbSig);

        // vtable slot 29 — EnumProperties (placeholder)
        [PreserveSig]
        int EnumProperties(
            ref IntPtr phEnum,
            uint td,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] uint[] rProperties,
            uint cMax,
            out uint pcProperties);

        // vtable slot 30 — EnumEvents (placeholder)
        [PreserveSig]
        int EnumEvents(
            ref IntPtr phEnum,
            uint td,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] uint[] rEvents,
            uint cMax,
            out uint pcEvents);

        // vtable slot 31 — GetEventProps (placeholder)
        [PreserveSig]
        int GetEventProps(
            uint ev,
            out uint pClass,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[] szEvent,
            uint cchEvent,
            out uint pchEvent,
            out uint pdwEventFlags,
            out uint ptkEventType,
            out uint pmdAddOn,
            out uint pmdRemoveOn,
            out uint pmdFire,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 11)] uint[] rmdOtherMethod,
            uint cMax,
            out uint pcOtherMethod);

        // vtable slot 32 — EnumMethodSemantics (placeholder)
        [PreserveSig]
        int EnumMethodSemantics(ref IntPtr phEnum, uint mb, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] uint[] rEventProp, uint cMax, out uint pcEventProp);

        // vtable slot 33 — GetMethodSemantics (placeholder)
        [PreserveSig]
        int GetMethodSemantics(uint mb, uint tkEventProp, out uint pdwSemanticsFlags);

        // vtable slot 34 — GetClassLayout (placeholder)
        [PreserveSig]
        int GetClassLayout(uint td, out uint pdwPackSize, IntPtr rFieldOffset, uint cMax, out uint pcFieldOffset, out uint pulClassSize);

        // vtable slot 35 — GetFieldMarshal (placeholder)
        [PreserveSig]
        int GetFieldMarshal(uint tk, out IntPtr ppvNativeType, out uint pcbNativeType);

        // vtable slot 36 — GetRVA (placeholder)
        [PreserveSig]
        int GetRVA(uint tk, out uint pulCodeRVA, out uint pdwImplFlags);

        // vtable slot 37 — GetPermissionSetProps (placeholder)
        [PreserveSig]
        int GetPermissionSetProps(uint pm, out uint pdwAction, out IntPtr ppvPermission, out uint pcbPermission);

        // vtable slot 38 — GetSigFromToken (placeholder)
        [PreserveSig]
        int GetSigFromToken(uint mdSig, out IntPtr ppvSig, out uint pcbSig);

        // vtable slot 39 — GetModuleRefProps (placeholder)
        [PreserveSig]
        int GetModuleRefProps(uint mur, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] char[] szName, uint cchName, out uint pchName);

        // vtable slot 40 — EnumModuleRefs (placeholder)
        [PreserveSig]
        int EnumModuleRefs(ref IntPtr phEnum, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] uint[] rModuleRefs, uint cmax, out uint pcModuleRefs);

        // vtable slot 41 — GetTypeSpecFromToken (placeholder)
        [PreserveSig]
        int GetTypeSpecFromToken(uint typespec, out IntPtr ppvSig, out uint pcbSig);

        // vtable slot 42 — GetNameFromToken (placeholder)
        [PreserveSig]
        int GetNameFromToken(uint tk, out IntPtr pszUtf8NamePtr);

        // vtable slot 43 — EnumUnresolvedMethods (placeholder)
        [PreserveSig]
        int EnumUnresolvedMethods(ref IntPtr phEnum, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] uint[] rMethods, uint cMax, out uint pcTokens);

        // vtable slot 44 — GetUserString (placeholder)
        [PreserveSig]
        int GetUserString(uint stk, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] char[] szString, uint cchString, out uint pchString);

        // vtable slot 45 — GetPinvokeMap (placeholder)
        [PreserveSig]
        int GetPinvokeMap(uint tk, out uint pdwMappingFlags, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[] szImportName, uint cchImportName, out uint pchImportName, out uint pmrImportDLL);

        // vtable slot 46 — EnumSignatures (placeholder)
        [PreserveSig]
        int EnumSignatures(ref IntPtr phEnum, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] uint[] rSignatures, uint cmax, out uint pcSignatures);

        // vtable slot 47 — EnumTypeSpecs (placeholder)
        [PreserveSig]
        int EnumTypeSpecs(ref IntPtr phEnum, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] uint[] rTypeSpecs, uint cmax, out uint pcTypeSpecs);

        // vtable slot 48 — EnumUserStrings (placeholder)
        [PreserveSig]
        int EnumUserStrings(ref IntPtr phEnum, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] uint[] rStrings, uint cmax, out uint pcStrings);

        // vtable slot 49 — GetParamForMethodIndex (placeholder)
        [PreserveSig]
        int GetParamForMethodIndex(uint md, uint ulParamSeq, out uint ppd);

        // vtable slot 50 — EnumCustomAttributes (placeholder)
        [PreserveSig]
        int EnumCustomAttributes(ref IntPtr phEnum, uint tk, uint tkType, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] uint[] rCustomAttributes, uint cMax, out uint pcCustomAttributes);

        // vtable slot 51 — GetCustomAttributeProps (placeholder)
        [PreserveSig]
        int GetCustomAttributeProps(uint cv, out uint ptkObj, out uint ptkType, out IntPtr ppBlob, out uint pcbSize);

        // vtable slot 52 — FindTypeRef (placeholder)
        [PreserveSig]
        int FindTypeRef(uint tkResolutionScope, [MarshalAs(UnmanagedType.LPWStr)] string szName, out uint ptr);

        // vtable slot 53 — GetMemberProps (placeholder)
        [PreserveSig]
        int GetMemberProps(uint mb, out uint pClass, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[] szMember, uint cchMember, out uint pchMember, out uint pdwAttr, out IntPtr ppvSigBlob, out uint pcbSigBlob, out uint pulCodeRVA, out uint pdwImplFlags, out uint pdwCPlusTypeFlag, out IntPtr ppValue, out uint pcchValue);

        // vtable slot 54 — GetFieldProps
        [PreserveSig]
        int GetFieldProps(
            uint mb,
            out uint pClass,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[] szField,
            uint cchField,
            out uint pchField,
            out uint pdwAttr,
            out IntPtr ppvSigBlob,
            out uint pcbSigBlob,
            out uint pdwCPlusTypeFlag,
            out IntPtr ppValue,
            out uint pcchValue);

        // vtable slot 55 — GetPropertyProps (placeholder)
        [PreserveSig]
        int GetPropertyProps(uint prop, out uint pClass, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[] szProperty, uint cchProperty, out uint pchProperty, out uint pdwPropFlags, out IntPtr ppvSig, out uint pbSig, out uint pdwCPlusTypeFlag, out IntPtr ppDefaultValue, out uint pcchDefaultValue, out uint pmdSetter, out uint pmdGetter, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 15)] uint[] rmdOtherMethod, uint cMax, out uint pcOtherMethod);

        // vtable slot 56 — GetParamProps (placeholder)
        [PreserveSig]
        int GetParamProps(uint tk, out uint pmd, out uint pulSequence, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] char[] szName, uint cchName, out uint pchName, out uint pdwAttr, out uint pdwCPlusTypeFlag, out IntPtr ppValue, out uint pcchValue);

        // vtable slot 57 — GetCustomAttributeByName (placeholder)
        [PreserveSig]
        int GetCustomAttributeByName(uint tkObj, [MarshalAs(UnmanagedType.LPWStr)] string szName, out IntPtr ppData, out uint pcbData);

        // vtable slot 58 — IsValidToken (placeholder)
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.Bool)]
        bool IsValidToken(uint tk);

        // vtable slot 59 — GetNestedClassProps (placeholder)
        [PreserveSig]
        int GetNestedClassProps(uint tdNestedClass, out uint ptdEnclosingClass);

        // vtable slot 60 — GetNativeCallConvFromSig (placeholder)
        [PreserveSig]
        int GetNativeCallConvFromSig(IntPtr pvSig, uint cbSig, out uint pCallConv);

        // vtable slot 61 — IsGlobal (placeholder)
        [PreserveSig]
        int IsGlobal(uint pd, out int pbGlobal);
    }

    // =========================================================================
    // DapVariable — DAP protocol variable representation
    // =========================================================================

    /// <summary>
    /// Represents a variable in the Debug Adapter Protocol (DAP) format.
    /// A non-zero VariablesReference means the value is expandable.
    /// </summary>
    public class DapVariable
    {
        /// <summary>Variable name (identifier or array index).</summary>
        public string Name { get; set; }

        /// <summary>Display value string.</summary>
        public string Value { get; set; }

        /// <summary>CLR type name (e.g. "Int32", "String", "MyClass").</summary>
        public string Type { get; set; }

        /// <summary>
        /// Non-zero when children can be fetched via GetChildren(variablesReference).
        /// Zero means the variable is a leaf (not expandable).
        /// </summary>
        public int VariablesReference { get; set; }
    }
}
