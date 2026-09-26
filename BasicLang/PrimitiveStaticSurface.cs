using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicLang
{
    /// <summary>
    /// The Shared members of the built-in type keywords — <c>String.Format</c>, <c>Integer.Parse</c>,
    /// <c>Integer.MaxValue</c>, <c>Double.IsNaN</c>, <c>Char.IsDigit</c> — that this compiler TYPES and
    /// that every native backend IMPLEMENTS. One table, the consumers:
    /// <list type="number">
    /// <item>SemanticAnalyzer.LookupNetTypeMember — the member's type, so
    /// <c>Dim n As Integer = Integer.Parse(s)</c> is an Integer rather than an Object that no typed
    /// store accepts.</item>
    /// <item>CSharpBackend — the receiver keyword respelled as C#'s (<c>Integer</c> is not a C# type).</item>
    /// <item>CppCodeGenerator + CppCapabilityChecker — the <c>BasicLang::Prim</c> runtime function, and a
    /// clean BL diagnostic for a keyword member NOT in this table.</item>
    /// <item>JavaScriptBackend — the on-demand prelude helper or constant.</item>
    /// </list>
    ///
    /// <para><b>Why not NativeBclSurface.</b> Every type name in that table becomes a "surface type",
    /// which routes its operators through the surface operator gates and pins it to
    /// BoundaryTypeRegistry.NativeOwned. <c>String</c> and <c>Integer</c> are neither: their operators
    /// are the language's own. This table carries Shared members and nothing else.</para>
    ///
    /// <para><b>Measured before this table</b> (master f8ad07c): <c>String.Format(...)</c> did not
    /// parse at all ("Unexpected token in expression: 'String'") — nor did any member of a type
    /// keyword. With the parse fixed, <c>String.Empty</c>, <c>Integer.Parse</c>, <c>Integer.MaxValue</c>,
    /// <c>Double.Parse</c>, <c>Char.IsDigit</c> and <c>String.Compare</c> all typed Object;
    /// <c>Integer.MaxValue</c> reached C# as <c>Integer.MaxValue</c> (CS0103); C++ had no lowering for
    /// any of them; and JavaScript printed <c>String.Empty &amp; "x"</c> as "undefinedx".</para>
    ///
    /// <para><b>Deliberately absent:</b> <c>String.Compare</c> (culture-aware collation — no native
    /// backend can reproduce ICU ordering, and an ordinal answer differs for "a" vs "B"), and
    /// <c>TryParse</c> (its out parameter is inexpressible, as NativeBclSurface also records).
    /// A keyword member absent here keeps the C# backend's permissive pass-through and is REFUSED on
    /// C++ (CppCapabilityChecker) and JavaScript rather than emitted as something that does not exist.</para>
    /// </summary>
    public static class PrimitiveStaticSurface
    {
        /// <summary>Parameter-count rule for a static method.</summary>
        public sealed class Row
        {
            public string TypeName { get; }
            public string MemberName { get; }
            public bool IsProperty { get; }
            /// <summary>Minimum argument count; a params member accepts any count ≥ this.</summary>
            public int MinArgs { get; }
            /// <summary>Maximum argument count; <see cref="int.MaxValue"/> for a params member.</summary>
            public int MaxArgs { get; }
            public string ReturnTypeName { get; }

            public Row(string typeName, string memberName, bool isProperty, int minArgs, int maxArgs, string returnTypeName)
            {
                TypeName = typeName;
                MemberName = memberName;
                IsProperty = isProperty;
                MinArgs = minArgs;
                MaxArgs = maxArgs;
                ReturnTypeName = returnTypeName;
            }

            public bool AcceptsArgCount(int count) => !IsProperty && count >= MinArgs && count <= MaxArgs;
        }

        /// <summary>The type keywords, BasicLang spelling → C# keyword.</summary>
        public static readonly IReadOnlyDictionary<string, string> CSharpKeyword =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["String"] = "string", ["Integer"] = "int", ["Long"] = "long", ["Short"] = "short",
                ["Byte"] = "byte", ["UByte"] = "byte", ["UShort"] = "ushort", ["UInteger"] = "uint",
                ["ULong"] = "ulong", ["Single"] = "float", ["Double"] = "double", ["Boolean"] = "bool",
                ["Char"] = "char",
            };

        /// <summary>The integral keywords whose Parse / MaxValue / MinValue are rows.</summary>
        public static readonly IReadOnlyList<string> IntegralTypes =
            new[] { "Integer", "Long", "Short", "Byte", "UShort", "UInteger", "ULong" };

        private const int Params = int.MaxValue;

        private static Row Method(string type, string name, int min, int max, string returns) =>
            new Row(type, name, false, min, max, returns);

        private static Row Property(string type, string name, string returns) =>
            new Row(type, name, true, 0, 0, returns);

        public static readonly IReadOnlyList<Row> Rows = BuildRows();

        private static List<Row> BuildRows()
        {
            var rows = new List<Row>
            {
                // String — Format / Join / Concat take a params list of values after the fixed part.
                Method("String", "Format", 1, Params, "String"),
                Method("String", "Join", 1, Params, "String"),
                Method("String", "Concat", 0, Params, "String"),
                Method("String", "IsNullOrEmpty", 1, 1, "Boolean"),
                Method("String", "IsNullOrWhiteSpace", 1, 1, "Boolean"),
                Property("String", "Empty", "String"),

                Method("Boolean", "Parse", 1, 1, "Boolean"),

                Method("Char", "IsDigit", 1, 1, "Boolean"),
                Method("Char", "IsLetter", 1, 1, "Boolean"),
                Method("Char", "IsLetterOrDigit", 1, 1, "Boolean"),
                Method("Char", "IsWhiteSpace", 1, 1, "Boolean"),
                Method("Char", "IsUpper", 1, 1, "Boolean"),
                Method("Char", "IsLower", 1, 1, "Boolean"),
                Method("Char", "ToUpper", 1, 1, "Char"),
                Method("Char", "ToLower", 1, 1, "Char"),
            };

            foreach (var t in IntegralTypes)
            {
                rows.Add(Method(t, "Parse", 1, 1, t));
                rows.Add(Property(t, "MaxValue", t));
                rows.Add(Property(t, "MinValue", t));
            }

            foreach (var t in new[] { "Double", "Single" })
            {
                rows.Add(Method(t, "Parse", 1, 1, t));
                rows.Add(Property(t, "MaxValue", t));
                rows.Add(Property(t, "MinValue", t));
                rows.Add(Property(t, "Epsilon", t));
                rows.Add(Property(t, "NaN", t));
                rows.Add(Property(t, "PositiveInfinity", t));
                rows.Add(Property(t, "NegativeInfinity", t));
                rows.Add(Method(t, "IsNaN", 1, 1, "Boolean"));
                rows.Add(Method(t, "IsInfinity", 1, 1, "Boolean"));
            }

            return rows;
        }

        private static readonly Dictionary<(string, string), Row> Index =
            Rows.ToDictionary(r => (r.TypeName.ToLowerInvariant(), r.MemberName.ToLowerInvariant()));

        /// <summary>True for a built-in type KEYWORD (the lexer's data-type group).</summary>
        public static bool IsTypeKeyword(string name) => name != null && CSharpKeyword.ContainsKey(name);

        public static bool TryGet(string typeName, string memberName, out Row row)
        {
            row = null;
            return typeName != null && memberName != null
                && Index.TryGetValue((typeName.ToLowerInvariant(), memberName.ToLowerInvariant()), out row);
        }

        /// <summary><paramref name="dottedName"/> (<c>Integer.Parse</c>) split and looked up.</summary>
        public static bool TryGetDotted(string dottedName, out Row row)
        {
            row = null;
            if (string.IsNullOrEmpty(dottedName)) return false;
            var dot = dottedName.LastIndexOf('.');
            if (dot <= 0 || dot >= dottedName.Length - 1) return false;
            return TryGet(dottedName.Substring(0, dot), dottedName.Substring(dot + 1), out row);
        }

        /// <summary>A dotted name whose receiver is a type keyword, whether or not the member is a row.</summary>
        public static bool IsKeywordReceiver(string dottedName, out string typeName, out string memberName)
        {
            typeName = memberName = null;
            if (string.IsNullOrEmpty(dottedName)) return false;
            var dot = dottedName.LastIndexOf('.');
            if (dot <= 0 || dot >= dottedName.Length - 1) return false;
            typeName = dottedName.Substring(0, dot);
            memberName = dottedName.Substring(dot + 1);
            return IsTypeKeyword(typeName);
        }
    }
}
