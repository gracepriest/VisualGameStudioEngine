using System;
using System.Collections.Generic;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Net
{
    /// <summary>
    /// Resolves a BasicLang type NAME to its fully-qualified .NET metadata spelling, in the
    /// caller's namespace context (§6.5's binding order). Shaped exactly like
    /// <c>SemanticAnalyzer.ResolveNetType</c>, which is the one production implementation —
    /// the projection cannot resolve names itself (it has no <c>Using</c> context and no
    /// resolver), so the environment hands the capability in.
    /// </summary>
    internal delegate NetTypeLookupOutcome NetTypeSpellingResolver(
        string name, int genericArgumentCount, out string fullName);

    /// <summary>
    /// THE §8.3+§6.4 argument-admissibility projection (P2a-2 Task 7a's carry-forward lift):
    /// which BasicLang static types are admissible .NET arguments, and how each is spelled in
    /// C# type syntax — <c>NetTypeResolver.ResolveOverload</c>'s argument grammar.
    ///
    /// <para><b>Why this lives in <c>BasicLang/Net/</c> and not in the analyzer.</b> The §8.3
    /// rows exist in three encodings that must agree — this projection (BL type → C# spelling),
    /// <c>NetProxyEmitter.WireOf</c> (C wire forms) and <c>NetShimGenerator.WireOf</c> (C#
    /// wire forms) — and Task 7a's call lowering is a fourth consumer. Private in
    /// <c>SemanticAnalyzer</c> it could only be re-derived; here it is consumed. Behavior is
    /// IDENTICAL to the pre-lift analyzer code (<c>NetStrictResolutionTests</c> is the proof).</para>
    ///
    /// <para>Environment-specific judgments stay with the caller, injected as capabilities:
    /// user-declared-type lookup (scope/global/project channels) and .NET name resolution
    /// (<c>Using</c> directives + the ambient set). This type owns only the projection.</para>
    /// </summary>
    internal static class NetMarshalTable
    {
        /// <summary>
        /// §8.3's by-value rows + §6.4's conversion pairs, as C# spellings. Everything else
        /// either resolves from metadata, is user-defined (BL6019), or is left untyped.
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, string> ArgumentSpellings =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Integer"] = "System.Int32",
                ["Long"] = "System.Int64",
                ["Short"] = "System.Int16",
                ["Byte"] = "System.Byte",
                ["SByte"] = "System.SByte",
                ["UByte"] = "System.Byte",
                ["UShort"] = "System.UInt16",
                ["UInteger"] = "System.UInt32",
                ["ULong"] = "System.UInt64",
                ["Single"] = "System.Single",
                ["Double"] = "System.Double",
                ["Boolean"] = "System.Boolean",
                ["Char"] = "System.Char",
                ["String"] = "System.String",
                // §6.4 conversion pairs (P1 NativeOwned values with managed counterparts).
                ["Decimal"] = "System.Decimal",
                ["DateTime"] = "System.DateTime",
                ["TimeSpan"] = "System.TimeSpan",
                ["Guid"] = "System.Guid",
                ["DateTimeOffset"] = "System.DateTimeOffset",
                ["StringBuilder"] = "System.Text.StringBuilder",
            };

        /// <summary>
        /// The REVERSE projection: the BasicLang spelling of a .NET metadata full name, for the
        /// §8.3/§6.4 rows that have one. Not a mechanical inverse of
        /// <see cref="ArgumentSpellings"/> — that map is not injective (<c>Byte</c> and
        /// <c>UByte</c> both spell <c>System.Byte</c>), so each row here names the CANONICAL
        /// BasicLang spelling. Used by the analyzer to type resolved-member RESULTS
        /// (<c>Dim ok = r.IsMatch("x")</c> → Boolean — lifting the flip's documented
        /// Object-degrade) and consulted by the lowering for representable returns. A full
        /// name outside this table has no by-value native representation: it is either a
        /// handle (NetRef) or not yet lowerable.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> BasicLangSpellings =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Int32"] = "Integer",
                ["System.Int64"] = "Long",
                ["System.Int16"] = "Short",
                ["System.Byte"] = "Byte",
                ["System.SByte"] = "SByte",
                ["System.UInt16"] = "UShort",
                ["System.UInt32"] = "UInteger",
                ["System.UInt64"] = "ULong",
                ["System.Single"] = "Single",
                ["System.Double"] = "Double",
                ["System.Boolean"] = "Boolean",
                ["System.Char"] = "Char",
                ["System.String"] = "String",
                ["System.Decimal"] = "Decimal",
                ["System.DateTime"] = "DateTime",
                ["System.TimeSpan"] = "TimeSpan",
                ["System.Guid"] = "Guid",
                ["System.DateTimeOffset"] = "DateTimeOffset",
                ["System.Text.StringBuilder"] = "StringBuilder",
            };

        /// <summary>See <see cref="BasicLangSpellings"/>.</summary>
        internal static bool TryGetBasicLangSpelling(string netTypeFullName, out string basicLangName)
        {
            basicLangName = null;
            return !string.IsNullOrEmpty(netTypeFullName)
                   && BasicLangSpellings.TryGetValue(netTypeFullName, out basicLangName);
        }

        /// <summary>
        /// The §6.4 pairs whose WIRE form is not one scalar slot: Decimal (the four-field
        /// GetBits quad), Guid (16 bytes), DateTimeOffset (the DECLARED scalar pair —
        /// blnet_marshal.hpp's ABI note), StringBuilder (directional — to-net only, as
        /// String). Their to_net_*/from_net_* converters exist (Task 6), but the two
        /// emitters plan one wire slot per parameter, so until Task 8's §8.3 drift
        /// completion adds multi-slot/directional machinery these are REFUSED at resolved
        /// call sites (BL6019 naming the parameter) rather than silently crossing as the
        /// handle row — a §6.4 value must never become a handle.
        /// </summary>
        internal static readonly IReadOnlyCollection<string> MultiSlotConversionPairs =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "System.Decimal",
                "System.Guid",
                "System.DateTimeOffset",
                "System.Text.StringBuilder",
            };

        /// <summary>
        /// True when a resolved member RESULT (or by-value parameter) of this full name has a
        /// native by-value representation Task 7a's lowering can carry across one wire slot:
        /// the §8.3 scalar/String rows plus the single-slot §6.4 pairs (DateTime, TimeSpan).
        /// </summary>
        internal static bool IsSingleSlotValue(string netTypeFullName) =>
            TryGetBasicLangSpelling(netTypeFullName, out _)
            && !MultiSlotConversionPairs.Contains(netTypeFullName);

        /// <summary>
        /// §6.5's admissible argument set — §8.3's rows plus §6.4's conversion pairs — projected
        /// onto C# type spellings. False means "do not ask": <paramref name="isUserDefined"/>
        /// distinguishes the BL6019 case (a user-declared class/structure/interface) from the
        /// silent leave-name-only cases. Moved VERBATIM from
        /// <c>SemanticAnalyzer.TryMapNetArgumentType</c> (P2a-2 Task 4); the two capability
        /// parameters are the judgments that could not move with it.
        /// </summary>
        internal static bool TryMapArgumentType(
            TypeInfo type,
            Func<string, bool> isUserDefinedTypeName,
            NetTypeSpellingResolver resolveNetType,
            out string spelling,
            out bool isUserDefined)
        {
            if (isUserDefinedTypeName == null) throw new ArgumentNullException(nameof(isUserDefinedTypeName));
            if (resolveNetType == null) throw new ArgumentNullException(nameof(resolveNetType));

            spelling = null;
            isUserDefined = false;

            if (type == null || string.IsNullOrEmpty(type.Name)) return false;

            // The analyzer's lost-precision fallback: Object is what an untypeable expression
            // degrades to, so treating it as a REAL System.Object argument would judge calls the
            // analyzer never actually typed. Object is also permanently Rejected (§6.4).
            if (string.Equals(type.Name, "Object", StringComparison.OrdinalIgnoreCase)) return false;

            switch (type.Kind)
            {
                case TypeKind.Pointer:
                case TypeKind.Foreign:
                case TypeKind.Delegate:
                case TypeKind.TypeParameter:
                case TypeKind.Tuple:
                case TypeKind.Void:
                case TypeKind.Nullable:
                    return false;
            }
            if (type.IsPointer || type.IsNullable || type.IsFixedLengthString) return false;

            if (type.Kind == TypeKind.Array)
            {
                if (type.ArrayRank > 1) return false;
                if (!TryMapArgumentType(type.ElementType, isUserDefinedTypeName, resolveNetType,
                        out var element, out isUserDefined))
                    return false;
                spelling = element + "[]";
                return true;
            }

            if (ArgumentSpellings.TryGetValue(type.Name, out var mapped))
            {
                spelling = mapped;
                return true;
            }

            // A user-declared type is checked BEFORE metadata resolution: `Class Timer` must
            // answer "user-defined" even though System.Threading.Timer would resolve — the
            // ambient-collision trap NetInertnessTests pins.
            if (isUserDefinedTypeName(type.Name))
            {
                isUserDefined = type.Kind == TypeKind.Class || type.Kind == TypeKind.Structure
                                || type.Kind == TypeKind.Interface;
                return false;
            }

            // A .NET-typed value (`Dim st As Stream` passed along): resolve the spelling the
            // same way the receiver resolved.
            var genericCount = type.GenericArguments?.Count ?? 0;
            if (resolveNetType(type.Name, genericCount, out var fullName)
                != NetTypeLookupOutcome.Resolved)
            {
                return false;
            }
            if (fullName.IndexOf('+') >= 0) return false;   // metadata-nested spelling: not C# syntax

            if (genericCount == 0)
            {
                spelling = fullName;
                return true;
            }

            var backtick = fullName.LastIndexOf('`');
            if (backtick < 0) return false;
            var argSpellings = new List<string>(genericCount);
            foreach (var typeArg in type.GenericArguments)
            {
                if (!TryMapArgumentType(typeArg, isUserDefinedTypeName, resolveNetType,
                        out var argSpelling, out _))
                    return false;
                argSpellings.Add(argSpelling);
            }
            spelling = fullName.Substring(0, backtick) + "<" + string.Join(", ", argSpellings) + ">";
            return true;
        }
    }
}
