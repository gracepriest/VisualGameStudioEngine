using System;
using System.Collections.Generic;
using System.Linq;
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
    /// HOW a §8.3/§6.4 row crosses, as opposed to WHAT it crosses as. The distinction the
    /// consumers actually branch on: a <see cref="Scalar"/> passes through untouched, a
    /// <see cref="Boolean"/> re-widths (C++ <c>bool</c> ↔ <c>int32</c>), a <see cref="Char"/>
    /// re-widths AND carries §14.10's lossy inbound narrowing, a <see cref="String"/> has
    /// direction-dependent ownership, and a <see cref="Conversion"/> row is a §6.4 pair whose
    /// value must pass through a named converter on both sides.
    /// </summary>
    internal enum NetWireShape { Scalar, Boolean, Char, String, Conversion }

    /// <summary>
    /// ONE row of spec §8.3 (plus §6.4's conversion pairs) — the P2a-2 Task-8 Step-0
    /// consolidation.
    ///
    /// <para><b>Why one row type exists at all.</b> Before this, the same rows lived in five
    /// independent encodings: <see cref="NetMarshalTable.ArgumentSpellings"/>, the reverse
    /// spelling map, <c>NetProxyEmitter.WireOf</c> (C), <c>NetShimGenerator.WireOf</c> (C#),
    /// and two hard-coded <c>switch</c> statements in <c>CppCodeGenerator.NetCalls.cs</c>.
    /// Only the two emitters were tied together by a test. A row present in the emitters but
    /// MISSING at the call site is not a compile error — it is a silent wire mismatch, which
    /// is the one failure mode this boundary cannot afford. The call site now projects from
    /// here, so adding a row in one place is what makes it exist.</para>
    ///
    /// <para><b><see cref="IsMultiSlot"/> is a property of the WIRE, not of the value.</b>
    /// Decimal is four scalars, Guid is sixteen bytes, DateTimeOffset is the declared scalar
    /// pair (never the padded struct — <c>blnet_marshal.hpp</c>'s ABI note), and StringBuilder
    /// is directional (to-net only). They are still §6.4 pairs and must never degrade to the
    /// handle row: a §6.4 value that becomes a handle is a silently wrong program.</para>
    /// </summary>
    /// <param name="NetFullName">The metadata full name this row is keyed by.</param>
    /// <param name="BasicLangSpelling">
    /// The CANONICAL BasicLang spelling. Not a mechanical inverse of
    /// <see cref="NetMarshalTable.ArgumentSpellings"/>, which is not injective (<c>Byte</c> and
    /// <c>UByte</c> both spell <c>System.Byte</c>).
    /// </param>
    /// <param name="NativeToNet">
    /// The native (C++) outbound converter's fully-qualified name, or null when the row needs
    /// none. <c>blnet_marshal.hpp</c> owns the definitions.
    /// </param>
    /// <param name="NativeFromNet">
    /// The native inbound converter, or null when the row has NO inbound direction —
    /// StringBuilder, whose §6.4 table row is explicitly one-way.
    /// </param>
    internal sealed record NetWireRow(
        string NetFullName,
        string BasicLangSpelling,
        NetWireShape Shape,
        bool IsMultiSlot = false,
        string NativeToNet = null,
        string NativeFromNet = null);

    /// <summary>
    /// The environment-specific JUDGMENTS <see cref="NetMarshalTable"/> cannot make for itself,
    /// bundled (P2a-2 Task-8 Step 0, M2).
    ///
    /// <para><b>Why a struct and not three parameters.</b> The projection recurses — arrays and
    /// generic type arguments both re-enter <see cref="NetMarshalTable.TryMapArgumentType"/> —
    /// so every capability had to be threaded through every recursion site by hand, and each
    /// new judgment widened the signature at four call sites at once. Task 8 adds the THIRD
    /// (an enum's underlying integral type, which §8.3's "enum → underlying integral" row needs
    /// and which no consumer can recover from a type NAME). Constructed ONCE per analyzer,
    /// which also stops the per-call method-group delegate allocation the old signature forced.</para>
    ///
    /// <para><b>Deliberately not defaultable.</b> A <c>default(NetTypeEnvironment)</c> carries
    /// null capabilities, and a projection that silently answered "not user-defined, does not
    /// resolve, not an enum" for everything would degrade every judgment to its most permissive
    /// answer. <see cref="RequireComplete"/> is the same guard the old null checks were, moved
    /// where it cannot be forgotten by a new consumer.</para>
    /// </summary>
    /// <param name="ResolveEnumUnderlyingType">
    /// The metadata full name of an enum's underlying integral type (<c>System.Int32</c> for
    /// <c>FileMode</c>), or null when the name is not an enum. §8.3's enum row.
    /// </param>
    internal readonly struct NetTypeEnvironment
    {
        private readonly Func<string, bool> _isUserDefinedTypeName;
        private readonly NetTypeSpellingResolver _resolveNetType;
        private readonly Func<string, string> _resolveEnumUnderlyingType;

        internal NetTypeEnvironment(
            Func<string, bool> isUserDefinedTypeName,
            NetTypeSpellingResolver resolveNetType,
            Func<string, string> resolveEnumUnderlyingType)
        {
            _isUserDefinedTypeName = isUserDefinedTypeName;
            _resolveNetType = resolveNetType;
            _resolveEnumUnderlyingType = resolveEnumUnderlyingType;
        }

        /// <summary>See the type remarks — a defaulted environment must never be consumed.</summary>
        internal void RequireComplete()
        {
            if (_isUserDefinedTypeName == null || _resolveNetType == null
                || _resolveEnumUnderlyingType == null)
            {
                throw new InvalidOperationException(
                    "NetTypeEnvironment was consumed without its capabilities. A defaulted "
                    + "environment would answer every judgment permissively — construct it from "
                    + "the analyzer's IsUserDefinedTypeName / ResolveNetType / "
                    + "NetEnumUnderlyingTypeFullName.");
            }
        }

        /// <summary>User-declared class / structure / interface lookup (scope, global, project).</summary>
        internal bool IsUserDefinedTypeName(string name) => _isUserDefinedTypeName(name);

        /// <summary>.NET name resolution in the caller's namespace context (§6.5's binding order).</summary>
        internal NetTypeLookupOutcome ResolveNetType(
            string name, int genericArgumentCount, out string fullName) =>
            _resolveNetType(name, genericArgumentCount, out fullName);

        /// <summary>§8.3's enum row: the underlying integral, or null for a non-enum.</summary>
        internal string ResolveEnumUnderlyingType(string netTypeFullName) =>
            _resolveEnumUnderlyingType(netTypeFullName);
    }

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
    /// <para>Environment-specific judgments stay with the caller, injected as one
    /// <see cref="NetTypeEnvironment"/>: user-declared-type lookup (scope/global/project
    /// channels), .NET name resolution (<c>Using</c> directives + the ambient set), and §8.3's
    /// enum underlying-integral lookup. This type owns only the projection.</para>
    /// </summary>
    internal static class NetMarshalTable
    {
        /// <summary>
        /// §8.3's by-value rows + §6.4's conversion pairs, as C# spellings. Everything else
        /// either resolves from metadata, is user-defined (BL6019), or is left untyped.
        ///
        /// <para><b>PRIVATE, and that is not tidiness.</b> Two of this map's properties are
        /// load-bearing and invisible from the raw dictionary: it is deliberately NOT injective
        /// (<c>Byte</c> and <c>UByte</c> both spell <c>System.Byte</c> — take the reverse
        /// direction from <see cref="TryGetBasicLangSpelling"/>, never by inverting this), and
        /// it is only ever consulted AFTER <c>Object</c> has been excluded and BEFORE the
        /// user-defined-type check, an ordering <see cref="TryMapArgumentType"/> owns. A caller
        /// reading the map directly would silently skip both.</para>
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> ArgumentSpellings =
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
        /// THE §8.3 + §6.4 row table, keyed by metadata full name — the single source every
        /// consumer projects from (see <see cref="NetWireRow"/> for why one table exists).
        /// Consumers: <c>NetProxyEmitter.WireOf</c> (the C column),
        /// <c>NetShimGenerator.WireOf</c> (the C# column), <c>CppCodeGenerator.NetCalls</c>
        /// (the call site), and the analyzer's §8.3 gate.
        ///
        /// <para>A full name ABSENT from this table has no by-value representation: it is
        /// either a handle (<c>NetRef</c>) or not lowerable at all. That default is the safe
        /// one — a handle is opaque, so being wrong about a type's shape costs a missing
        /// convenience, never a misinterpreted 64 bits.</para>
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, NetWireRow> WireRows =
            new Dictionary<string, NetWireRow>(StringComparer.Ordinal)
            {
                // §8.3's by-value rows.
                ["System.Boolean"] = new("System.Boolean", "Boolean", NetWireShape.Boolean),
                ["System.SByte"] = new("System.SByte", "SByte", NetWireShape.Scalar),
                ["System.Byte"] = new("System.Byte", "Byte", NetWireShape.Scalar),
                ["System.Int16"] = new("System.Int16", "Short", NetWireShape.Scalar),
                ["System.UInt16"] = new("System.UInt16", "UShort", NetWireShape.Scalar),
                ["System.Int32"] = new("System.Int32", "Integer", NetWireShape.Scalar),
                ["System.UInt32"] = new("System.UInt32", "UInteger", NetWireShape.Scalar),
                ["System.Int64"] = new("System.Int64", "Long", NetWireShape.Scalar),
                ["System.UInt64"] = new("System.UInt64", "ULong", NetWireShape.Scalar),
                ["System.Single"] = new("System.Single", "Single", NetWireShape.Scalar),
                ["System.Double"] = new("System.Double", "Double", NetWireShape.Scalar),
                ["System.Char"] = new("System.Char", "Char", NetWireShape.Char),
                ["System.String"] = new("System.String", "String", NetWireShape.String),

                // §6.4's conversion pairs. Converter names are blnet_marshal.hpp's
                // (CppNetMarshal.cs) verbatim — the ONE place they are spelled for the
                // lowering, so a rename there cannot leave a call site emitting a
                // now-nonexistent function.
                ["System.DateTime"] = new(
                    "System.DateTime", "DateTime", NetWireShape.Conversion,
                    NativeToNet: "BasicLang::net::to_net_datetime",
                    NativeFromNet: "BasicLang::net::from_net_datetime"),
                ["System.TimeSpan"] = new(
                    "System.TimeSpan", "TimeSpan", NetWireShape.Conversion,
                    NativeToNet: "BasicLang::net::to_net_timespan",
                    NativeFromNet: "BasicLang::net::from_net_timespan"),
                ["System.Decimal"] = new(
                    "System.Decimal", "Decimal", NetWireShape.Conversion, IsMultiSlot: true,
                    NativeToNet: "BasicLang::net::to_net_decimal",
                    NativeFromNet: "BasicLang::net::from_net_decimal"),
                ["System.Guid"] = new(
                    "System.Guid", "Guid", NetWireShape.Conversion, IsMultiSlot: true,
                    NativeToNet: "BasicLang::net::to_net_guid",
                    NativeFromNet: "BasicLang::net::from_net_guid"),
                ["System.DateTimeOffset"] = new(
                    "System.DateTimeOffset", "DateTimeOffset", NetWireShape.Conversion,
                    IsMultiSlot: true,
                    NativeToNet: "BasicLang::net::to_net_datetimeoffset",
                    NativeFromNet: "BasicLang::net::from_net_datetimeoffset"),
                // DIRECTIONAL: §6.4's table gives StringBuilder a to-net direction only, so
                // NativeFromNet is null ON PURPOSE and a StringBuilder RESULT must refuse.
                // to_net_stringbuilder's absence of an inverse is pinned by a fast test.
                ["System.Text.StringBuilder"] = new(
                    "System.Text.StringBuilder", "StringBuilder", NetWireShape.Conversion,
                    IsMultiSlot: true,
                    NativeToNet: "BasicLang::net::to_net_stringbuilder",
                    NativeFromNet: null),
            };

        /// <summary>The <see cref="WireRows"/> row for a full name, or false.</summary>
        internal static bool TryGetWireRow(string netTypeFullName, out NetWireRow row)
        {
            row = null;
            return !string.IsNullOrEmpty(netTypeFullName)
                   && WireRows.TryGetValue(netTypeFullName, out row);
        }

        /// <summary>
        /// The REVERSE projection: the CANONICAL BasicLang spelling of a .NET metadata full
        /// name, for the §8.3/§6.4 rows that have one. Used by the analyzer to type
        /// resolved-member RESULTS (<c>Dim ok = r.IsMatch("x")</c> → Boolean — lifting the
        /// flip's documented Object-degrade) and consulted by the lowering for representable
        /// returns. A full name outside <see cref="WireRows"/> has no by-value native
        /// representation: it is either a handle (NetRef) or not yet lowerable.
        /// </summary>
        internal static bool TryGetBasicLangSpelling(string netTypeFullName, out string basicLangName)
        {
            basicLangName = TryGetWireRow(netTypeFullName, out var row) ? row.BasicLangSpelling : null;
            return basicLangName != null;
        }

        /// <summary>
        /// The §6.4 pairs whose WIRE form is not one scalar slot: Decimal (the four-field
        /// GetBits quad), Guid (16 bytes), DateTimeOffset (the DECLARED scalar pair —
        /// blnet_marshal.hpp's ABI note), StringBuilder (directional — to-net only, as
        /// String). Projected from <see cref="WireRows"/> rather than re-listed, so the two
        /// cannot disagree about which rows are multi-slot.
        /// </summary>
        internal static readonly IReadOnlyCollection<string> MultiSlotConversionPairs =
            new HashSet<string>(
                WireRows.Values.Where(r => r.IsMultiSlot).Select(r => r.NetFullName),
                StringComparer.Ordinal);

        /// <summary>
        /// True when a resolved member RESULT (or by-value parameter) of this full name has a
        /// native by-value representation the lowering can carry across ONE wire slot: the
        /// §8.3 scalar/String rows plus the single-slot §6.4 pairs (DateTime, TimeSpan).
        /// </summary>
        internal static bool IsSingleSlotValue(string netTypeFullName) =>
            TryGetWireRow(netTypeFullName, out var row) && !row.IsMultiSlot;

        /// <summary>
        /// §6.5's admissible argument set — §8.3's rows plus §6.4's conversion pairs — projected
        /// onto C# type spellings. False means "do not ask": <paramref name="isUserDefined"/>
        /// distinguishes the BL6019 case (a user-declared class/structure/interface) from the
        /// silent leave-name-only cases. Moved VERBATIM from
        /// <c>SemanticAnalyzer.TryMapNetArgumentType</c> (P2a-2 Task 4);
        /// <paramref name="environment"/> carries the judgments that could not move with it.
        /// </summary>
        internal static bool TryMapArgumentType(
            TypeInfo type,
            in NetTypeEnvironment environment,
            out string spelling,
            out bool isUserDefined)
        {
            environment.RequireComplete();

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
                if (!TryMapArgumentType(type.ElementType, environment, out var element, out isUserDefined))
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
            if (environment.IsUserDefinedTypeName(type.Name))
            {
                isUserDefined = type.Kind == TypeKind.Class || type.Kind == TypeKind.Structure
                                || type.Kind == TypeKind.Interface;
                return false;
            }

            // A .NET-typed value (`Dim st As Stream` passed along): resolve the spelling the
            // same way the receiver resolved.
            var genericCount = type.GenericArguments?.Count ?? 0;
            if (environment.ResolveNetType(type.Name, genericCount, out var fullName)
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
                if (!TryMapArgumentType(typeArg, environment, out var argSpelling, out _))
                    return false;
                argSpellings.Add(argSpelling);
            }
            spelling = fullName.Substring(0, backtick) + "<" + string.Join(", ", argSpellings) + ">";
            return true;
        }
    }
}
