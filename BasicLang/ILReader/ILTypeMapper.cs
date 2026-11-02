using System;
using System.Collections.Generic;

namespace BasicLang.Compiler.ILReader
{
    /// <summary>
    /// Maps .NET IL type names to C++ type names.
    /// Phase 1: primitive types + common BCL types. Reuses MSILBackend's _typeMap patterns.
    /// </summary>
    public static class ILTypeMapper
    {
        private static readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase)
        {
            // Primitives (full .NET names)
            ["System.Boolean"] = "bool",
            ["System.Byte"] = "uint8_t",
            // Primitives (IL short names, as emitted by TypeSpec signatures)
            ["bool"] = "bool",
            ["char"] = "char",
            ["int8"] = "int8_t",
            ["uint8"] = "uint8_t",
            ["int16"] = "int16_t",
            ["uint16"] = "uint16_t",
            ["int"] = "int32_t",
            ["int32"] = "int32_t",
            ["uint"] = "uint32_t",
            ["uint32"] = "uint32_t",
            ["long"] = "int64_t",
            ["int64"] = "int64_t",
            ["ulong"] = "uint64_t",
            ["uint64"] = "uint64_t",
            ["float"] = "float",
            ["float32"] = "float",
            ["double"] = "double",
            ["float64"] = "double",
            ["string"] = "std::string",
            ["System.SByte"] = "int8_t",
            ["System.Char"] = "char",
            ["System.Int16"] = "int16_t",
            ["System.UInt16"] = "uint16_t",
            ["System.Int32"] = "int32_t",
            ["System.UInt32"] = "uint32_t",
            ["System.Int64"] = "int64_t",
            ["System.UInt64"] = "uint64_t",
            ["System.Single"] = "float",
            ["System.Double"] = "double",
            ["System.Decimal"] = "double",  // approximate; VGS C++ has no native Decimal
            ["System.String"] = "std::string",
            ["System.Object"] = "void*",     // opaque pointer
            ["object"] = "void*",            // IL short name
            ["System.Void"] = "void",
            ["System.IntPtr"] = "void*",

            // Common BCL (mapped to VGS C++ runtime types where possible)
            ["System.DateTime"] = "int64_t",  // ticks
            ["System.TimeSpan"] = "int64_t",
            ["System.Guid"] = "std::string",  // stringified
            ["System.TimeSpan"] = "int64_t",  // ticks
            ["System.Guid"] = "std::string",  // stringified
            ["System.Exception"] = "void*",
            ["System.ArgumentException"] = "void*",
            ["System.NullReferenceException"] = "void*",
        };

        /// <summary>
        /// Map an IL type name to a C++ type. Falls back to the sanitized name for unknown types.
        /// </summary>
        public static string Map(string ilTypeName)
        {
            if (string.IsNullOrEmpty(ilTypeName)) return "void";

            // Handle arrays: "System.Int32[]" → "int32_t*"
            if (ilTypeName.EndsWith("[]", StringComparison.Ordinal))
            {
                var elem = ilTypeName.Substring(0, ilTypeName.Length - 2);
                return Map(elem) + "*";
            }

            // Handle generics: "System.Collections.Generic.List`1<System.Int32>" → "BasicLang::List<int32_t>"
            if (ilTypeName.Contains("<") && ilTypeName.Contains(">"))
            {
                var baseName = ilTypeName.Substring(0, ilTypeName.IndexOf('<'));
                var argsStart = ilTypeName.IndexOf('<');
                var argsEnd = ilTypeName.LastIndexOf('>');
                var args = ilTypeName.Substring(argsStart + 1, argsEnd - argsStart - 1);

                // Split top-level generic args (handle nested)
                var argList = SplitGenericArgs(args);
                var mappedArgs = string.Join(", ", argList.ConvertAll(Map));

                switch (baseName)
                {
                    case "System.Collections.Generic.List`1":
                        return $"BasicLang::List<{mappedArgs}>";
                    case "System.Collections.Generic.Dictionary`2":
                        return $"BasicLang::Dictionary<{mappedArgs}>";
                    case "System.Collections.Generic.HashSet`1":
                        return $"BasicLang::HashSet<{mappedArgs}>";
                    default:
                        return $"/* {ilTypeName} */ void*";
                }
            }

            // Handle by-ref: "System.Int32&" → "int32_t&"
            if (ilTypeName.EndsWith("&", StringComparison.Ordinal))
            {
                return Map(ilTypeName.Substring(0, ilTypeName.Length - 1)) + "&";
            }

            // Handle pointers
            if (ilTypeName.EndsWith("*", StringComparison.Ordinal))
            {
                return Map(ilTypeName.Substring(0, ilTypeName.Length - 1)) + "*";
            }

            if (_map.TryGetValue(ilTypeName, out var cppType))
                return cppType;

            // Unknown type — sanitize and use as class name (will need a definition)
            return Sanitize(ilTypeName);
        }

        private static List<string> SplitGenericArgs(string args)
        {
            var result = new List<string>();
            int depth = 0;
            var current = new System.Text.StringBuilder();
            foreach (char c in args)
            {
                if (c == '<') { depth++; current.Append(c); }
                else if (c == '>') { depth--; current.Append(c); }
                else if (c == ',' && depth == 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                else current.Append(c);
            }
            if (current.Length > 0) result.Add(current.ToString().Trim());
            return result;
        }

        private static string Sanitize(string name)
        {
            // Remove namespace, keep last segment
            var lastDot = name.LastIndexOf('.');
            if (lastDot >= 0) name = name.Substring(lastDot + 1);
            // Remove backtick generic arity
            var backtick = name.IndexOf('`');
            if (backtick >= 0) name = name.Substring(0, backtick);
            return name;
        }
    }
}