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

        // Start high so variable reference IDs can never collide with the
        // adapter's scope reference IDs (which start at 1 and are checked first
        // in the variables request) — a collision makes child expansion of the
        // first variables return scope contents instead of the object's fields.
        private const int RefIdBase = 1_000_000;
        private int _nextRefId = RefIdBase;

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
                // Raw COM pointer registered by the raw-vtable inspection path
                if (storedValue is IntPtr rawPtr && rawPtr != IntPtr.Zero)
                    return RawGetChildren(rawPtr);

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
        ///
        /// Accepts either an ICorDebugILFrame RCW or a boxed IntPtr to the raw
        /// ICorDebugILFrame COM pointer (managed casts of mscordbi objects fail
        /// unpredictably on .NET Core, so the raw-vtable path is preferred).
        ///
        /// Understands compiler-generated code from lambdas and async functions:
        ///  - Async state-machine frames (&lt;Foo&gt;d__N.MoveNext): locals hoisted to
        ///    fields like "&lt;n&gt;5__2" are surfaced with their original names; the
        ///    raw compiler temp slots and plumbing fields are hidden.
        ///  - Closure frames (&lt;&gt;c__DisplayClass...): captured variables (fields
        ///    of 'this') are surfaced as locals with their original names.
        ///  - Normal frames whose locals include a closure container
        ///    ("CS$&lt;&gt;8__locals0"): the captured fields are appended as
        ///    additional top-level locals.
        /// </summary>
        public List<DapVariable> GetLocals(object corDebugILFrame)
        {
            // Preferred: raw-vtable inspection (robust against RCW cast failures)
            var ctx = FrameContext.Acquire(corDebugILFrame);
            if (ctx != null)
            {
                using (ctx)
                {
                    try { return RawGetLocals(ctx); }
                    catch (Exception ex)
                    {
                        return new List<DapVariable> { MakeVariable("<error>", ex.Message, "Error", 0) };
                    }
                }
            }

            // Legacy fallback: managed COM interfaces
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
        /// Get arguments from a stack frame. Accepts an ICorDebugILFrame RCW or a
        /// boxed IntPtr (see GetLocals). Parameters are named from metadata; the
        /// compiler-generated 'this' of closure frames is hidden, a real 'this'
        /// is shown as "Me", and for async state-machine frames the hoisted
        /// parameters (plain-named fields of the state machine) are returned.
        /// </summary>
        public List<DapVariable> GetArguments(object corDebugILFrame)
        {
            var ctx = FrameContext.Acquire(corDebugILFrame);
            if (ctx != null)
            {
                using (ctx)
                {
                    try { return RawGetArguments(ctx); }
                    catch (Exception ex)
                    {
                        return new List<DapVariable> { MakeVariable("<error>", ex.Message, "Error", 0) };
                    }
                }
            }

            // Legacy fallback: managed COM interfaces
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
        /// Releases any raw COM pointers registered for child expansion.
        /// </summary>
        public void ClearReferences()
        {
            foreach (var value in _variableReferences.Values)
            {
                if (value is IntPtr ptr && ptr != IntPtr.Zero)
                {
                    try { Marshal.Release(ptr); } catch { }
                }
            }
            _variableReferences.Clear();
            _nextRefId = RefIdBase;
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

        // =========================================================================
        // Raw-vtable inspection engine
        //
        // mscordbi's ICorDebug* objects reject standard RCW interface casts on
        // .NET Core in many contexts ("Unable to cast COM object"), while raw
        // IUnknown::QueryInterface + direct vtable calls work reliably. This
        // engine performs all frame/value access through raw vtable calls and
        // understands the compiler-generated shapes produced for lambdas
        // (closure display classes) and async functions (state machines).
        // =========================================================================

        private static readonly Guid IID_ICorDebugILFrame       = new Guid("03E26311-4F76-11D3-88C6-006097945418");
        private static readonly Guid IID_ICorDebugReferenceValue = new Guid("CC7BCAF9-8A68-11D2-983C-0000F808342D");
        private static readonly Guid IID_ICorDebugGenericValue  = new Guid("CC7BCAF8-8A68-11D2-983C-0000F808342D");
        private static readonly Guid IID_ICorDebugStringValue   = new Guid("CC7BCAF1-8A68-11D2-983C-0000F808342D");
        private static readonly Guid IID_ICorDebugObjectValue   = new Guid("18AD3D6E-B7D2-11D2-BD04-0000F80849BD");
        private static readonly Guid IID_ICorDebugBoxedValue    = new Guid("CC7BCAF3-8A68-11D2-983C-0000F808342D");
        private static readonly Guid IID_ICorDebugArrayValue    = new Guid("0405B0DF-A660-11D2-BD02-0000F80849BD");
        private static readonly Guid IID_IMetaDataImport        = new Guid("7DAC8207-D3AE-4C75-9B67-92801A497D44");

        // Field attribute flags (CorHdr.h)
        private const uint FdStatic  = 0x0010;
        private const uint FdLiteral = 0x0040;
        // Method attribute flags
        private const uint MdStatic  = 0x0010;

        // --- Raw delegate shapes -------------------------------------------------

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int D_GetUInt(IntPtr self, out uint value);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int D_GetInt(IntPtr self, out int value);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int D_GetPtr(IntPtr self, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int D_GetIndexedPtr(IntPtr self, uint index, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int D_GetBuffer(IntPtr self, IntPtr dest);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int D_GetChars(IntPtr self, uint cch, out uint pcch,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] char[] buffer);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int D_GetMetaData(IntPtr self, ref Guid riid, out IntPtr ppObj);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int D_GetFieldValue(IntPtr self, IntPtr pClass, uint fieldDef, out IntPtr ppValue);

        private static T Fn<T>(IntPtr comPtr, int slot) where T : Delegate =>
            (T)Marshal.GetDelegateForFunctionPointer(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(comPtr), slot * IntPtr.Size), typeof(T));

        private static bool QI(IntPtr comPtr, Guid iid, out IntPtr result)
        {
            result = IntPtr.Zero;
            if (comPtr == IntPtr.Zero) return false;
            try
            {
                int hr = Marshal.QueryInterface(comPtr, ref iid, out result);
                if (hr < 0 || result == IntPtr.Zero) { result = IntPtr.Zero; return false; }
                return true;
            }
            catch { result = IntPtr.Zero; return false; }
        }

        private static void SafeRelease(IntPtr ptr)
        {
            if (ptr != IntPtr.Zero)
            {
                try { Marshal.Release(ptr); } catch { }
            }
        }

        // --- Frame context -------------------------------------------------------

        /// <summary>
        /// Resolved raw-frame state: the ICorDebugILFrame pointer plus method/type
        /// identity resolved from metadata. Owns one reference to FramePtr.
        /// </summary>
        private sealed class FrameContext : IDisposable
        {
            public IntPtr FramePtr;
            public int FunctionToken;
            public uint DeclTypeToken;
            public uint MethodAttrs;
            public string DeclTypeName = string.Empty;
            public string MethodName = string.Empty;
            public string ModulePath = string.Empty;
            public IMetaDataImport Metadata;

            /// <summary>
            /// Accepts an ICorDebugILFrame RCW or a boxed IntPtr; returns null if
            /// the raw ICorDebugILFrame interface cannot be obtained.
            /// </summary>
            public static FrameContext Acquire(object frame)
            {
                IntPtr unk = IntPtr.Zero;
                bool ownUnk = false;
                try
                {
                    if (frame is IntPtr p && p != IntPtr.Zero)
                    {
                        unk = p; // borrowed
                    }
                    else if (frame != null)
                    {
                        try { unk = Marshal.GetIUnknownForObject(frame); ownUnk = true; }
                        catch { return null; }
                    }
                    if (unk == IntPtr.Zero) return null;

                    if (!QI(unk, IID_ICorDebugILFrame, out var framePtr))
                        return null;

                    var ctx = new FrameContext { FramePtr = framePtr };
                    ctx.Populate();
                    return ctx;
                }
                finally
                {
                    if (ownUnk) SafeRelease(unk);
                }
            }

            private void Populate()
            {
                try
                {
                    // ICorDebugFrame::GetFunctionToken — slot 6
                    if (Fn<D_GetUInt>(FramePtr, 6)(FramePtr, out uint token) >= 0)
                        FunctionToken = (int)token;

                    // ICorDebugFrame::GetFunction — slot 5
                    if (Fn<D_GetPtr>(FramePtr, 5)(FramePtr, out var funcPtr) < 0 || funcPtr == IntPtr.Zero)
                        return;
                    try
                    {
                        // ICorDebugFunction::GetClass — slot 4
                        if (Fn<D_GetPtr>(funcPtr, 4)(funcPtr, out var classPtr) >= 0 && classPtr != IntPtr.Zero)
                        {
                            try
                            {
                                // ICorDebugClass::GetToken — slot 4
                                if (Fn<D_GetUInt>(classPtr, 4)(classPtr, out uint typeTok) >= 0)
                                    DeclTypeToken = typeTok;
                            }
                            finally { SafeRelease(classPtr); }
                        }

                        // ICorDebugFunction::GetModule — slot 3
                        if (Fn<D_GetPtr>(funcPtr, 3)(funcPtr, out var modulePtr) >= 0 && modulePtr != IntPtr.Zero)
                        {
                            try
                            {
                                ModulePath = RawGetModuleName(modulePtr);
                                Metadata = RawGetMetadata(modulePtr);
                            }
                            finally { SafeRelease(modulePtr); }
                        }
                    }
                    finally { SafeRelease(funcPtr); }

                    if (Metadata != null)
                    {
                        if (DeclTypeToken != 0)
                            DeclTypeName = GetTypeNameFromToken(Metadata, DeclTypeToken) ?? string.Empty;
                        (MethodName, MethodAttrs) = GetMethodNameAndAttrs(Metadata, (uint)FunctionToken);
                    }
                }
                catch { /* keep whatever was resolved */ }
            }

            public void Dispose()
            {
                SafeRelease(FramePtr);
                FramePtr = IntPtr.Zero;
            }
        }

        private static string RawGetModuleName(IntPtr modulePtr)
        {
            try
            {
                // ICorDebugModule::GetName — slot 6
                var buffer = new char[1024];
                if (Fn<D_GetChars>(modulePtr, 6)(modulePtr, (uint)buffer.Length, out uint len, buffer) >= 0 && len > 1)
                    return new string(buffer, 0, (int)len - 1);
            }
            catch { }
            return string.Empty;
        }

        private static IMetaDataImport RawGetMetadata(IntPtr modulePtr)
        {
            try
            {
                // ICorDebugModule::GetMetaDataInterface — slot 14
                Guid iid = IID_IMetaDataImport;
                if (Fn<D_GetMetaData>(modulePtr, 14)(modulePtr, ref iid, out var mdPtr) >= 0 && mdPtr != IntPtr.Zero)
                {
                    try { return Marshal.GetObjectForIUnknown(mdPtr) as IMetaDataImport; }
                    finally { SafeRelease(mdPtr); }
                }
            }
            catch { }
            return null;
        }

        private static string GetTypeNameFromToken(IMetaDataImport mdImport, uint typeToken)
        {
            try
            {
                var buffer = new char[1024];
                int hr = mdImport.GetTypeDefProps(typeToken, buffer, (uint)buffer.Length,
                    out uint len, out _, out _);
                if (hr >= 0 && len > 1)
                    return new string(buffer, 0, (int)len - 1);
            }
            catch { }
            return null;
        }

        private static (string name, uint attrs) GetMethodNameAndAttrs(IMetaDataImport mdImport, uint methodToken)
        {
            try
            {
                var buffer = new char[512];
                int hr = mdImport.GetMethodProps(methodToken, out _, buffer, (uint)buffer.Length,
                    out uint len, out uint attrs, out _, out _, out _, out _);
                if (hr >= 0 && len > 1)
                    return (new string(buffer, 0, (int)len - 1), attrs);
            }
            catch { }
            return (string.Empty, 0);
        }

        // --- PDB name cache ------------------------------------------------------

        // Module path → SourceMapper (null when no PDB is available for the module)
        private readonly Dictionary<string, SourceMapper> _pdbByModule =
            new(StringComparer.OrdinalIgnoreCase);

        private Dictionary<int, string> GetPdbLocalNames(FrameContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.ModulePath))
                return new Dictionary<int, string>();

            if (!_pdbByModule.TryGetValue(ctx.ModulePath, out var mapper))
            {
                mapper = new SourceMapper();
                bool ok = false;
                try
                {
                    var pdbPath = System.IO.Path.ChangeExtension(ctx.ModulePath, ".pdb");
                    ok = System.IO.File.Exists(pdbPath) && mapper.LoadPdb(pdbPath);
                }
                catch { }
                if (!ok) { mapper.Dispose(); mapper = null; }
                _pdbByModule[ctx.ModulePath] = mapper;
            }

            return mapper?.GetLocalVariableNames(ctx.FunctionToken) ?? new Dictionary<int, string>();
        }

        // --- Locals --------------------------------------------------------------

        private List<DapVariable> RawGetLocals(FrameContext ctx)
        {
            bool isStateMachine = GeneratedNames.IsStateMachineType(ctx.DeclTypeName);
            bool isDisplayClass = GeneratedNames.IsDisplayClassType(ctx.DeclTypeName);

            // Async state-machine frame: user locals live as hoisted fields
            // ("<n>5__2") on 'this'; the IL slot locals are compiler temps.
            if (isStateMachine)
            {
                var hoisted = GetThisFields(ctx, HoistedKind.HoistedLocals);
                if (hoisted.Count > 0)
                    return hoisted;
                // else fall through to slot locals (better than nothing)
            }

            var pdbNames = GetPdbLocalNames(ctx);
            var result = new List<DapVariable>();
            var closureExpansions = new List<int>(); // variablesReference of display-class locals

            // ICorDebugILFrame::EnumerateLocalVariables — slot 13
            uint count = 0;
            if (Fn<D_GetPtr>(ctx.FramePtr, 13)(ctx.FramePtr, out var enumPtr) >= 0 && enumPtr != IntPtr.Zero)
            {
                try
                {
                    // ICorDebugEnum::GetCount — slot 6
                    Fn<D_GetUInt>(enumPtr, 6)(enumPtr, out count);
                }
                finally { SafeRelease(enumPtr); }
            }

            for (uint i = 0; i < count; i++)
            {
                bool hasPdbName = pdbNames.TryGetValue((int)i, out var pdbName);

                // In a state-machine frame without hoisted fields available we
                // list raw slots; in normal frames unnamed slots are compiler
                // temps but are kept (positionally) so PDB-based renaming and
                // setVariable by index still line up.
                IntPtr valPtr = IntPtr.Zero;
                DapVariable dapVar;
                try
                {
                    // ICorDebugILFrame::GetLocalVariable — slot 14
                    int hr = Fn<D_GetIndexedPtr>(ctx.FramePtr, 14)(ctx.FramePtr, i, out valPtr);
                    dapVar = (hr >= 0 && valPtr != IntPtr.Zero)
                        ? RawInspect(valPtr, hasPdbName ? pdbName : $"local_{i}")
                        : MakeVariable(hasPdbName ? pdbName : $"local_{i}", "<unavailable>", "Object", 0);
                }
                finally { SafeRelease(valPtr); }

                // A closure container local ("CS$<>8__locals0", filtered out of
                // pdbNames) — give it a friendly name and remember to surface
                // its captured fields as top-level locals below.
                if (!hasPdbName && GeneratedNames.IsDisplayClassType(dapVar.Type))
                {
                    dapVar.Name = "(closure)";
                    if (dapVar.VariablesReference > 0)
                        closureExpansions.Add(dapVar.VariablesReference);
                }

                result.Add(dapVar);
            }

            // Closure frame (lambda body): captured variables are fields of 'this'.
            if (isDisplayClass)
            {
                foreach (var captured in GetThisFields(ctx, HoistedKind.All))
                {
                    if (!result.Exists(v => v.Name == captured.Name))
                        result.Add(captured);
                }
            }

            // Normal frame with closure containers: append the captured fields
            // as top-level locals (after the positional slot list).
            foreach (var refId in closureExpansions)
            {
                foreach (var field in GetChildren(refId))
                {
                    if (!GeneratedNames.IsPlumbingFieldName(field.Name) &&
                        !result.Exists(v => v.Name == field.Name))
                    {
                        result.Add(field);
                    }
                }
            }

            return result;
        }

        // --- Arguments -----------------------------------------------------------

        private List<DapVariable> RawGetArguments(FrameContext ctx)
        {
            bool isStateMachine = GeneratedNames.IsStateMachineType(ctx.DeclTypeName);
            bool isDisplayClass = GeneratedNames.IsDisplayClassType(ctx.DeclTypeName);

            // Async state-machine frame: the original parameters were hoisted to
            // plain-named fields on the state machine.
            if (isStateMachine)
                return GetThisFields(ctx, HoistedKind.HoistedParameters);

            var result = new List<DapVariable>();
            bool hasThis = (ctx.MethodAttrs & MdStatic) == 0;
            var paramNames = GetParamNamesFromMetadata(ctx);

            // ICorDebugILFrame::EnumerateArguments — slot 15
            uint count = 0;
            if (Fn<D_GetPtr>(ctx.FramePtr, 15)(ctx.FramePtr, out var enumPtr) >= 0 && enumPtr != IntPtr.Zero)
            {
                try { Fn<D_GetUInt>(enumPtr, 6)(enumPtr, out count); }
                finally { SafeRelease(enumPtr); }
            }

            for (uint i = 0; i < count; i++)
            {
                string name;
                if (i == 0 && hasThis)
                {
                    // Hide the compiler-generated 'this' of closure classes;
                    // show a real receiver as "Me".
                    if (isDisplayClass || GeneratedNames.IsCompilerGeneratedType(ctx.DeclTypeName))
                        continue;
                    name = "Me";
                }
                else
                {
                    int paramIndex = hasThis ? (int)i - 1 : (int)i;
                    name = paramNames.TryGetValue(paramIndex, out var pn) ? pn : $"arg_{i}";
                }

                IntPtr valPtr = IntPtr.Zero;
                try
                {
                    // ICorDebugILFrame::GetArgument — slot 16
                    int hr = Fn<D_GetIndexedPtr>(ctx.FramePtr, 16)(ctx.FramePtr, i, out valPtr);
                    result.Add(hr >= 0 && valPtr != IntPtr.Zero
                        ? RawInspect(valPtr, name)
                        : MakeVariable(name, "<unavailable>", "Object", 0));
                }
                finally { SafeRelease(valPtr); }
            }

            return result;
        }

        /// <summary>Parameter names (0-based, excluding 'this') from metadata.</summary>
        private static Dictionary<int, string> GetParamNamesFromMetadata(FrameContext ctx)
        {
            var result = new Dictionary<int, string>();
            if (ctx.Metadata == null) return result;

            try
            {
                for (uint seq = 1; seq <= 64; seq++)
                {
                    int hr = ctx.Metadata.GetParamForMethodIndex((uint)ctx.FunctionToken, seq, out uint paramToken);
                    if (hr < 0 || paramToken == 0)
                        break;

                    var buffer = new char[512];
                    hr = ctx.Metadata.GetParamProps(paramToken, out _, out uint sequence,
                        buffer, (uint)buffer.Length, out uint len, out _, out _, out _, out _);
                    if (hr >= 0 && len > 1 && sequence >= 1)
                        result[(int)sequence - 1] = new string(buffer, 0, (int)len - 1);
                }
            }
            catch { }
            return result;
        }

        // --- Hoisted/captured field extraction ------------------------------------

        private enum HoistedKind
        {
            /// <summary>Hoisted locals: fields named "&lt;x&gt;5__N" (plus "&lt;&gt;4__this" as Me).</summary>
            HoistedLocals,
            /// <summary>Hoisted parameters: plain-named, non-plumbing fields.</summary>
            HoistedParameters,
            /// <summary>Everything except plumbing (closure captures).</summary>
            All
        }

        /// <summary>
        /// Reads the instance fields of the frame's 'this' argument (state machine
        /// or closure object), demangles their names and filters out plumbing.
        /// </summary>
        private List<DapVariable> GetThisFields(FrameContext ctx, HoistedKind kind)
        {
            var result = new List<DapVariable>();
            if (ctx.Metadata == null || ctx.DeclTypeToken == 0)
                return result;

            IntPtr thisArg = IntPtr.Zero;
            try
            {
                // ICorDebugILFrame::GetArgument(0) — slot 16
                int hr = Fn<D_GetIndexedPtr>(ctx.FramePtr, 16)(ctx.FramePtr, 0, out thisArg);
                if (hr < 0 || thisArg == IntPtr.Zero)
                    return result;

                IntPtr objPtr = RawResolveToObject(thisArg);
                if (objPtr == IntPtr.Zero)
                    return result;

                try
                {
                    result = ReadObjectFieldsRaw(objPtr, ctx.Metadata, ctx.DeclTypeToken, kind);
                }
                finally { SafeRelease(objPtr); }
            }
            catch { }
            finally { SafeRelease(thisArg); }

            return result;
        }

        /// <summary>
        /// Follows reference/boxed wrappers until an ICorDebugObjectValue is
        /// reached. Returns an owned pointer (caller releases) or IntPtr.Zero.
        /// </summary>
        private static IntPtr RawResolveToObject(IntPtr valuePtr)
        {
            if (valuePtr == IntPtr.Zero) return IntPtr.Zero;

            if (QI(valuePtr, IID_ICorDebugReferenceValue, out var refPtr))
            {
                try
                {
                    // ICorDebugReferenceValue::IsNull — slot 7
                    if (Fn<D_GetInt>(refPtr, 7)(refPtr, out int isNull) < 0 || isNull != 0)
                        return IntPtr.Zero;
                    // ICorDebugReferenceValue::Dereference — slot 10
                    if (Fn<D_GetPtr>(refPtr, 10)(refPtr, out var derefPtr) < 0 || derefPtr == IntPtr.Zero)
                        return IntPtr.Zero;
                    try { return RawResolveToObject(derefPtr); }
                    finally { SafeRelease(derefPtr); }
                }
                finally { SafeRelease(refPtr); }
            }

            if (QI(valuePtr, IID_ICorDebugBoxedValue, out var boxPtr))
            {
                try
                {
                    // ICorDebugBoxedValue::GetObject — slot 9
                    if (Fn<D_GetPtr>(boxPtr, 9)(boxPtr, out var innerPtr) >= 0 && innerPtr != IntPtr.Zero)
                        return innerPtr; // already an object value (owned)
                }
                finally { SafeRelease(boxPtr); }
                return IntPtr.Zero;
            }

            if (QI(valuePtr, IID_ICorDebugObjectValue, out var objPtr))
                return objPtr; // owned

            return IntPtr.Zero;
        }

        /// <summary>
        /// Enumerates the instance fields of an object via metadata + raw
        /// GetFieldValue calls, applying the HoistedKind filter and demangling.
        /// </summary>
        private List<DapVariable> ReadObjectFieldsRaw(IntPtr objPtr, IMetaDataImport mdImport,
            uint typeToken, HoistedKind kind)
        {
            var result = new List<DapVariable>();

            // ICorDebugObjectValue::GetClass — slot 7 (needed for GetFieldValue)
            if (Fn<D_GetPtr>(objPtr, 7)(objPtr, out var classPtr) < 0 || classPtr == IntPtr.Zero)
                return result;

            IntPtr hEnum = IntPtr.Zero;
            try
            {
                var fieldTokens = new uint[64];
                while (true)
                {
                    int hr = mdImport.EnumFields(ref hEnum, typeToken, fieldTokens, (uint)fieldTokens.Length, out uint fetched);
                    if (hr < 0 || fetched == 0)
                        break;

                    for (uint i = 0; i < fetched; i++)
                    {
                        uint fieldToken = fieldTokens[i];
                        var (fieldName, fieldAttrs) = GetFieldNameAndAttrs(mdImport, fieldToken);

                        if (string.IsNullOrEmpty(fieldName))
                            continue;
                        if ((fieldAttrs & (FdStatic | FdLiteral)) != 0)
                            continue;

                        bool isHoistedLocal = fieldName.Length > 1 && fieldName[0] == '<' &&
                                              !fieldName.StartsWith("<>", StringComparison.Ordinal);
                        bool isHoistedThis = fieldName.StartsWith("<>4__this", StringComparison.Ordinal);
                        bool isPlumbing = GeneratedNames.IsPlumbingFieldName(fieldName);

                        bool include = kind switch
                        {
                            HoistedKind.HoistedLocals => isHoistedLocal || isHoistedThis,
                            HoistedKind.HoistedParameters => !isPlumbing && !isHoistedLocal && !isHoistedThis,
                            _ => !isPlumbing
                        };
                        if (!include)
                            continue;

                        string displayName = GeneratedNames.DemangleMemberName(fieldName);

                        IntPtr fieldValPtr = IntPtr.Zero;
                        try
                        {
                            // ICorDebugObjectValue::GetFieldValue — slot 8
                            hr = Fn<D_GetFieldValue>(objPtr, 8)(objPtr, classPtr, fieldToken, out fieldValPtr);
                            result.Add(hr >= 0 && fieldValPtr != IntPtr.Zero
                                ? RawInspect(fieldValPtr, displayName)
                                : MakeVariable(displayName, "<unavailable>", "Object", 0));
                        }
                        finally { SafeRelease(fieldValPtr); }
                    }

                    if (fetched < (uint)fieldTokens.Length)
                        break;
                }
            }
            catch { }
            finally
            {
                if (hEnum != IntPtr.Zero)
                {
                    try { mdImport.CloseEnum(hEnum); } catch { }
                }
                SafeRelease(classPtr);
            }

            return result;
        }

        private static (string name, uint attrs) GetFieldNameAndAttrs(IMetaDataImport mdImport, uint fieldToken)
        {
            try
            {
                var buffer = new char[512];
                int hr = mdImport.GetFieldProps(fieldToken, out _, buffer, (uint)buffer.Length,
                    out uint len, out uint attrs, out _, out _, out _, out _, out _);
                if (hr >= 0 && len > 1)
                    return (new string(buffer, 0, (int)len - 1), attrs);
            }
            catch { }
            return (null, 0);
        }

        // --- Raw value inspection --------------------------------------------------

        /// <summary>
        /// Inspect a raw ICorDebugValue pointer (borrowed reference; this method
        /// takes its own references as needed for child expansion).
        /// </summary>
        private DapVariable RawInspect(IntPtr valuePtr, string name, int depth = 0)
        {
            if (valuePtr == IntPtr.Zero || depth > 8)
                return MakeVariable(name, "Nothing", "Object", 0);

            try
            {
                // Reference → dereference (or Nothing)
                if (QI(valuePtr, IID_ICorDebugReferenceValue, out var refPtr))
                {
                    try
                    {
                        if (Fn<D_GetInt>(refPtr, 7)(refPtr, out int isNull) < 0 || isNull != 0)
                            return MakeVariable(name, "Nothing", "Object", 0);
                        if (Fn<D_GetPtr>(refPtr, 10)(refPtr, out var derefPtr) < 0 || derefPtr == IntPtr.Zero)
                            return MakeVariable(name, "Nothing", "Object", 0);
                        try { return RawInspect(derefPtr, name, depth + 1); }
                        finally { SafeRelease(derefPtr); }
                    }
                    finally { SafeRelease(refPtr); }
                }

                // Boxed value → unwrap
                if (QI(valuePtr, IID_ICorDebugBoxedValue, out var boxPtr))
                {
                    try
                    {
                        if (Fn<D_GetPtr>(boxPtr, 9)(boxPtr, out var innerPtr) >= 0 && innerPtr != IntPtr.Zero)
                        {
                            try { return RawInspect(innerPtr, name, depth + 1); }
                            finally { SafeRelease(innerPtr); }
                        }
                        return MakeVariable(name, "<boxed>", "Object", 0);
                    }
                    finally { SafeRelease(boxPtr); }
                }

                // String
                if (QI(valuePtr, IID_ICorDebugStringValue, out var strPtr))
                {
                    try { return RawInspectString(strPtr, name); }
                    finally { SafeRelease(strPtr); }
                }

                // Array
                if (QI(valuePtr, IID_ICorDebugArrayValue, out var arrPtr))
                {
                    try
                    {
                        // ICorDebugArrayValue::GetElementType — slot 9; GetCount — slot 11
                        int elemType = 0;
                        uint count = 0;
                        Fn<D_GetInt>(arrPtr, 9)(arrPtr, out elemType);
                        Fn<D_GetUInt>(arrPtr, 11)(arrPtr, out count);
                        string elemTypeName = CorElementTypeToTypeName((CorElementType)elemType);
                        string typeName = $"{elemTypeName}({count})";
                        int refId = count > 0 ? RegisterRawReference(arrPtr) : 0;
                        return MakeVariable(name, typeName, typeName, refId);
                    }
                    finally { SafeRelease(arrPtr); }
                }

                // Object (class or struct instance)
                if (QI(valuePtr, IID_ICorDebugObjectValue, out var objPtr))
                {
                    try
                    {
                        string typeName = RawGetObjectTypeName(objPtr) ?? "Object";
                        typeName = MapToBasicLangTypeName(typeName);
                        int refId = RegisterRawReference(objPtr);
                        return MakeVariable(name, $"{{{typeName}}}", typeName, refId);
                    }
                    finally { SafeRelease(objPtr); }
                }

                // Primitive
                if (QI(valuePtr, IID_ICorDebugGenericValue, out var genPtr))
                {
                    try { return RawInspectPrimitive(valuePtr, genPtr, name); }
                    finally { SafeRelease(genPtr); }
                }

                // Fallback — type info only
                int fallbackType = 0;
                try { Fn<D_GetInt>(valuePtr, 3)(valuePtr, out fallbackType); } catch { }
                var et = (CorElementType)fallbackType;
                return MakeVariable(name, $"<{et}>", et.ToString(), 0);
            }
            catch (Exception ex)
            {
                return MakeVariable(name, $"<error: {ex.Message}>", "Error", 0);
            }
        }

        private static DapVariable RawInspectString(IntPtr strPtr, string name)
        {
            // ICorDebugStringValue::GetLength — slot 9; GetString — slot 10
            if (Fn<D_GetUInt>(strPtr, 9)(strPtr, out uint length) < 0)
                return MakeVariable(name, "<unreadable string>", "String", 0);
            if (length == 0)
                return MakeVariable(name, "\"\"", "String", 0);

            var buffer = new char[length + 1];
            if (Fn<D_GetChars>(strPtr, 10)(strPtr, length + 1, out uint fetched, buffer) < 0)
                return MakeVariable(name, "<unreadable string>", "String", 0);

            string value = new string(buffer, 0, (int)Math.Min(fetched, length));
            string escaped = EscapeString(value);
            string preview = escaped.Length > 100 ? escaped.Substring(0, 100) + "..." : escaped;
            return MakeVariable(name, $"\"{preview}\"", "String", 0);
        }

        private static DapVariable RawInspectPrimitive(IntPtr valuePtr, IntPtr genPtr, string name)
        {
            // ICorDebugValue::GetType — slot 3; GetSize — slot 4 (on the base value)
            int elemTypeRaw = 0;
            uint size = 0;
            Fn<D_GetInt>(valuePtr, 3)(valuePtr, out elemTypeRaw);
            Fn<D_GetUInt>(valuePtr, 4)(valuePtr, out size);
            var elemType = (CorElementType)elemTypeRaw;

            if (size == 0 || size > 64)
                return MakeVariable(name, "<empty>", elemType.ToString(), 0);

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                // ICorDebugGenericValue::GetValue — slot 7
                if (Fn<D_GetBuffer>(genPtr, 7)(genPtr, buffer) < 0)
                    return MakeVariable(name, "<unreadable>", elemType.ToString(), 0);

                var bytes = new byte[size];
                Marshal.Copy(buffer, bytes, 0, (int)size);
                string display = ReadPrimitiveValue(elemType, bytes);
                return MakeVariable(name, display, CorElementTypeToTypeName(elemType), 0);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static string RawGetObjectTypeName(IntPtr objPtr)
        {
            // ICorDebugObjectValue::GetClass — slot 7
            if (Fn<D_GetPtr>(objPtr, 7)(objPtr, out var classPtr) < 0 || classPtr == IntPtr.Zero)
                return null;
            try
            {
                // ICorDebugClass::GetToken — slot 4; GetModule — slot 3
                if (Fn<D_GetUInt>(classPtr, 4)(classPtr, out uint typeToken) < 0)
                    return null;
                if (Fn<D_GetPtr>(classPtr, 3)(classPtr, out var modulePtr) < 0 || modulePtr == IntPtr.Zero)
                    return null;
                try
                {
                    var mdImport = RawGetMetadata(modulePtr);
                    if (mdImport == null) return null;
                    string fullName = GetTypeNameFromToken(mdImport, typeToken);
                    if (string.IsNullOrEmpty(fullName)) return null;
                    int dot = fullName.LastIndexOf('.');
                    return dot >= 0 ? fullName.Substring(dot + 1) : fullName;
                }
                finally { SafeRelease(modulePtr); }
            }
            finally { SafeRelease(classPtr); }
        }

        // --- Raw children (object fields / array elements) -------------------------

        private List<DapVariable> RawGetChildren(IntPtr rawPtr)
        {
            var result = new List<DapVariable>();

            // Object fields
            if (QI(rawPtr, IID_ICorDebugObjectValue, out var objPtr))
            {
                try
                {
                    // Resolve type + metadata from the object's class
                    if (Fn<D_GetPtr>(objPtr, 7)(objPtr, out var classPtr) >= 0 && classPtr != IntPtr.Zero)
                    {
                        try
                        {
                            if (Fn<D_GetUInt>(classPtr, 4)(classPtr, out uint typeToken) >= 0 &&
                                Fn<D_GetPtr>(classPtr, 3)(classPtr, out var modulePtr) >= 0 && modulePtr != IntPtr.Zero)
                            {
                                try
                                {
                                    var mdImport = RawGetMetadata(modulePtr);
                                    if (mdImport != null)
                                        return ReadObjectFieldsRaw(objPtr, mdImport, typeToken, HoistedKind.All);
                                }
                                finally { SafeRelease(modulePtr); }
                            }
                        }
                        finally { SafeRelease(classPtr); }
                    }
                }
                finally { SafeRelease(objPtr); }
                return result;
            }

            // Array elements
            if (QI(rawPtr, IID_ICorDebugArrayValue, out var arrPtr))
            {
                try
                {
                    Fn<D_GetUInt>(arrPtr, 11)(arrPtr, out uint count);
                    uint limit = Math.Min(count, 100);
                    for (uint i = 0; i < limit; i++)
                    {
                        IntPtr elemPtr = IntPtr.Zero;
                        try
                        {
                            // ICorDebugArrayValue::GetElementAtPosition — slot 16
                            int hr = Fn<D_GetIndexedPtr>(arrPtr, 16)(arrPtr, i, out elemPtr);
                            result.Add(hr >= 0 && elemPtr != IntPtr.Zero
                                ? RawInspect(elemPtr, $"[{i}]")
                                : MakeVariable($"[{i}]", "<unavailable>", "Object", 0));
                        }
                        finally { SafeRelease(elemPtr); }
                    }
                    if (count > limit)
                        result.Add(MakeVariable("...", $"({count - limit} more elements)", "", 0));
                }
                finally { SafeRelease(arrPtr); }
            }

            return result;
        }

        // --- Frame name resolution (for stack traces) -------------------------------

        /// <summary>
        /// Resolves a user-facing frame name from a raw ICorDebugILFrame/ICorDebugFrame
        /// pointer by reading the method + declaring type from the frame's OWN module
        /// metadata (method tokens are only unique per module — searching other
        /// modules produces garbage names). Compiler-generated names are demangled:
        /// "&lt;&gt;c__DisplayClass0_0.&lt;Main&gt;b__0" → "Main.&lt;lambda&gt;",
        /// "&lt;WorkAsync&gt;d__1.MoveNext" → "WorkAsync".
        /// Returns null if resolution fails (callers should fall back).
        /// </summary>
        public static string ResolveFrameName(IntPtr framePtr)
        {
            if (framePtr == IntPtr.Zero) return null;
            try
            {
                using var ctx = FrameContext.Acquire(framePtr);
                if (ctx == null || string.IsNullOrEmpty(ctx.MethodName))
                    return null;

                string typeName = ctx.DeclTypeName ?? string.Empty;
                // Strip namespace for display
                int dot = typeName.LastIndexOf('.');
                string simpleType = dot >= 0 ? typeName.Substring(dot + 1) : typeName;

                string full = string.IsNullOrEmpty(simpleType)
                    ? ctx.MethodName
                    : $"{simpleType}.{ctx.MethodName}";

                return GeneratedNames.DemangleFrameName(full) ?? full;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Registers a raw COM pointer for child expansion (takes a reference).</summary>
        private int RegisterRawReference(IntPtr ptr)
        {
            try { Marshal.AddRef(ptr); } catch { return 0; }
            int refId = _nextRefId++;
            _variableReferences[refId] = ptr;
            return refId;
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
