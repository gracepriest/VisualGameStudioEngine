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
    /// <para><b><see cref="SlotCount"/> is a property of the WIRE, not of the value.</b>
    /// Decimal is four scalars, DateTimeOffset is the declared scalar pair (never the padded
    /// struct — <c>blnet_marshal.hpp</c>'s ABI note). They are still §6.4 pairs and must never
    /// degrade to the handle row: a §6.4 value that becomes a handle is a silently wrong
    /// program.</para>
    ///
    /// <para><b>⛔ Why this is a COUNT and not the <c>IsMultiSlot</c> boolean it replaced.</b>
    /// That flag was read as "refuse", and it was set on four rows for three unrelated reasons:
    /// Decimal/DateTimeOffset genuinely need more than one slot; Guid needs ONE slot whose C
    /// type differs by direction (<c>const uint8_t*</c> in, <c>uint8_t*</c> out — precisely the
    /// shape <c>String</c> already had); StringBuilder needs one slot in ONE direction. Conflating
    /// them meant the two rows that fit the existing one-slot machinery were refused alongside the
    /// two that do not. The count now says only what it means, and the other two facts have their
    /// own carriers (<see cref="COutWire"/>, and a null <see cref="NativeFromNet"/>).</para>
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
    /// <param name="CWire">
    /// The C ABI spelling of this row's single wire slot — the same string
    /// <c>NetProxyEmitter.WireOf</c> puts in its <c>CType</c> column, tied to it row by row by
    /// <c>NetProxyEmitterTests.EveryWireRowsCTypeMatchesTheEmittedSlot</c>. Null for the rows
    /// that do not have ONE by-value slot: String (two, with opposite ownership per direction)
    /// and every multi-slot §6.4 pair.
    ///
    /// <para><b>Its PRESENCE and its VALUE are used for different things, and only the presence
    /// is universal.</b> Presence answers "does this row have a single by-value wire slot?",
    /// which is the §8.3 ByRef gate in both the analyzer and the lowering. The VALUE is read
    /// only by the ByRef path's converted-temporary arm — Char and the single-slot §6.4 pairs —
    /// where the proxy's C++ parameter type happens to be this same string.</para>
    ///
    /// <para><b>⚠ It is NOT, in general, the type a C++ temporary must be declared as.</b>
    /// <c>Boolean</c> is the row where the two diverge: it crosses the ABI as <c>int32_t</c>
    /// (a C++ <c>bool</c> is not blittable for <c>[UnmanagedCallersOnly]</c>) while
    /// <c>NetProxyEmitter</c> shapes its ByRef parameter as <c>bool&amp;</c>. Nothing binds an
    /// <c>int32_t</c> temporary to that, which is exactly why the lowering's Scalar/Boolean arm
    /// hands over the native variable and RETURNS before the temporary path. A future
    /// generalization that needs the bindable type must take the proxy's C++ parameter type,
    /// never this.</para>
    /// </param>
    /// <param name="ConverterForm">
    /// HOW <paramref name="NativeToNet"/> is invoked at a call site. The converters are not
    /// uniform and the difference is not inferable from the type name, so it is recorded here
    /// rather than re-derived by each emitter — the same consolidation argument as the rest of
    /// this row.
    /// </param>
    /// <param name="NativeTempDecl">
    /// For the forms that need a caller-owned temporary, its C++ declaration with <c>{0}</c>
    /// where the name goes. Null for <see cref="NetConverterForm.Expression"/>.
    /// </param>
    /// <param name="COutWire">
    /// The C ABI spelling this row's slot takes in the OUT direction, when that differs from
    /// <paramref name="CWire"/>. Null means "same as in". Only the pointer-shaped rows need it:
    /// an inbound Guid borrows 16 bytes the caller owns (<c>const uint8_t*</c>), an outbound one
    /// is written THROUGH (<c>uint8_t*</c>). <c>String</c> has the same asymmetry but keeps its
    /// dedicated <see cref="NetWireShape.String"/> arm, because its two directions differ in
    /// OWNERSHIP as well as constness.
    /// </param>
    /// <summary>
    /// How a §6.4 row's native converter is CALLED. Three shapes exist in
    /// <c>blnet_marshal.hpp</c> and the lowering must not guess between them.
    /// </summary>
    internal enum NetConverterForm
    {
        /// <summary>
        /// <c>slot = to_net(x)</c> — the converter RETURNS the wire value, so the argument is a
        /// pure expression needing no statements. DateTime, TimeSpan.
        /// </summary>
        Expression,

        /// <summary>
        /// <c>decl buf; to_net(x, buf);</c> — the converter returns <c>void</c> and WRITES
        /// THROUGH a caller-owned buffer, which is then what crosses. Guid
        /// (<c>to_net_guid(const Guid&amp;, uint8_t out[16])</c>).
        /// </summary>
        OutBuffer,

        /// <summary>
        /// <c>decl t = to_net(x);</c> and <c>t.c_str()</c> crosses. The converter returns an
        /// OWNING <c>std::string</c>, so binding <c>.c_str()</c> directly to the call would
        /// dangle — the temporary dies at the end of its full-expression while the callee still
        /// holds the pointer. StringBuilder.
        /// </summary>
        OwningTemp,
    }

    /// <summary>
    /// ONE wire slot of a multi-slot §6.4 row.
    ///
    /// <para><c>NativeField</c> is the field of the struct <c>NativeToNet</c> returns — and it
    /// is NOT the P1 field of the same name. <c>NetDecimalWire</c> spells them <c>lo, mid, hi,
    /// flags</c> while <c>BasicLang::Decimal</c> spells them <c>lo_, mid_, hi_, flags_</c>
    /// (<c>CppDecimalRuntime.cs:207</c>), and <c>to_net_decimal</c>'s own body reads the
    /// underscored ones — so the natural mistake compiles nowhere but reads correctly.</para>
    /// </summary>
    internal sealed record NetWireSlot(string CWire, string COutWire, string NativeField);

    internal sealed record NetWireRow(
        string NetFullName,
        string BasicLangSpelling,
        NetWireShape Shape,
        int SlotCount = 1,
        string NativeToNet = null,
        string NativeFromNet = null,
        string CWire = null,
        string COutWire = null,
        NetConverterForm ConverterForm = NetConverterForm.Expression,
        string NativeTempDecl = null,
        IReadOnlyList<NetWireSlot> Slots = null)
    {
        /// <summary>
        /// More than one wire slot — Decimal and DateTimeOffset. The one-slot-per-parameter
        /// machinery in both emitters cannot carry these, so they still refuse.
        /// </summary>
        internal bool IsMultiSlot => SlotCount > 1;

        /// <summary>
        /// §8.3's ByRef gate: this row crosses as ONE slot holding a by-value scalar, so a
        /// <c>ref</c>/<c>out</c> parameter of it is just a pointer to that scalar.
        ///
        /// <para><b>The pointer test is load-bearing, not cosmetic.</b> Guid and StringBuilder
        /// now HAVE a <see cref="CWire"/>, which the old gate (<c>IsMultiSlot ||
        /// CWire is null</c>) took as sufficient. But their slot is already a pointer, so a
        /// ByRef one would be a pointer-to-pointer with no specified ownership — exactly what
        /// §8.3 declines to define. Testing the spelling keeps them refused while the by-value
        /// direction lowers.</para>
        /// </summary>
        internal bool HasByValueScalarSlot =>
            CWire != null && !CWire.EndsWith("*", StringComparison.Ordinal);

        /// <summary>
        /// This row spreads across several discrete scalar slots, and <see cref="Slots"/>
        /// describes each. Kept as its own predicate rather than reading
        /// <see cref="SlotCount"/> so the two can never drift: the count is what the refusal
        /// MESSAGES quote, the list is what the emitters BUILD FROM.
        /// </summary>
        internal bool HasSlotList => Slots is { Count: > 1 };
    }

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
                // §8.3's by-value rows. CWire is NetProxyEmitter.WireOf's C column — the two
                // must agree, and the §8.3 drift test compares them row by row.
                ["System.Boolean"] = new(
                    "System.Boolean", "Boolean", NetWireShape.Boolean, CWire: "int32_t"),
                ["System.SByte"] = new(
                    "System.SByte", "SByte", NetWireShape.Scalar, CWire: "int8_t"),
                ["System.Byte"] = new(
                    "System.Byte", "Byte", NetWireShape.Scalar, CWire: "uint8_t"),
                ["System.Int16"] = new(
                    "System.Int16", "Short", NetWireShape.Scalar, CWire: "int16_t"),
                ["System.UInt16"] = new(
                    "System.UInt16", "UShort", NetWireShape.Scalar, CWire: "uint16_t"),
                ["System.Int32"] = new(
                    "System.Int32", "Integer", NetWireShape.Scalar, CWire: "int32_t"),
                ["System.UInt32"] = new(
                    "System.UInt32", "UInteger", NetWireShape.Scalar, CWire: "uint32_t"),
                ["System.Int64"] = new(
                    "System.Int64", "Long", NetWireShape.Scalar, CWire: "int64_t"),
                ["System.UInt64"] = new(
                    "System.UInt64", "ULong", NetWireShape.Scalar, CWire: "uint64_t"),
                ["System.Single"] = new(
                    "System.Single", "Single", NetWireShape.Scalar, CWire: "float"),
                ["System.Double"] = new(
                    "System.Double", "Double", NetWireShape.Scalar, CWire: "double"),
                ["System.Char"] = new(
                    "System.Char", "Char", NetWireShape.Char, CWire: "uint16_t"),
                // String's two directions have different C types (const char* in, char** out)
                // and different OWNERSHIP, so there is no single CWire and a ByRef String slot
                // is refused rather than guessed — the same reason NetProxyEmitter refuses it.
                ["System.String"] = new("System.String", "String", NetWireShape.String),

                // §6.4's conversion pairs. Converter names are blnet_marshal.hpp's
                // (CppNetMarshal.cs) verbatim — the ONE place they are spelled for the
                // lowering, so a rename there cannot leave a call site emitting a
                // now-nonexistent function.
                ["System.DateTime"] = new(
                    "System.DateTime", "DateTime", NetWireShape.Conversion,
                    NativeToNet: "BasicLang::net::to_net_datetime",
                    NativeFromNet: "BasicLang::net::from_net_datetime",
                    CWire: "uint64_t"),
                ["System.TimeSpan"] = new(
                    "System.TimeSpan", "TimeSpan", NetWireShape.Conversion,
                    NativeToNet: "BasicLang::net::to_net_timespan",
                    NativeFromNet: "BasicLang::net::from_net_timespan",
                    CWire: "int64_t"),
                // The two genuinely multi-slot pairs. Decimal is the four-uint32 GetBits quad;
                // DateTimeOffset the DECLARED (int64 utcTicks, int16 offsetMinutes) pair — never
                // NetDateTimeOffsetWire itself, which is sizeof 16 with SIX trailing padding
                // bytes and must not cross the ABI by value (blnet_marshal.hpp's ABI note).
                //
                // ⛔ CWire stays NULL on both. They have no ONE by-value slot, which is exactly
                // what HasByValueScalarSlot asks — inventing one to satisfy a test would make
                // ByRef Decimal reachable and emit a struct into a scalar temporary.
                ["System.Decimal"] = new(
                    "System.Decimal", "Decimal", NetWireShape.Conversion, SlotCount: 4,
                    NativeToNet: "BasicLang::net::to_net_decimal",
                    NativeFromNet: "BasicLang::net::from_net_decimal",
                    Slots: new[]
                    {
                        new NetWireSlot("uint32_t", "uint32_t*", "lo"),
                        new NetWireSlot("uint32_t", "uint32_t*", "mid"),
                        new NetWireSlot("uint32_t", "uint32_t*", "hi"),
                        new NetWireSlot("uint32_t", "uint32_t*", "flags"),
                    }),
                ["System.DateTimeOffset"] = new(
                    "System.DateTimeOffset", "DateTimeOffset", NetWireShape.Conversion,
                    SlotCount: 2,
                    NativeToNet: "BasicLang::net::to_net_datetimeoffset",
                    NativeFromNet: "BasicLang::net::from_net_datetimeoffset",
                    Slots: new[]
                    {
                        new NetWireSlot("int64_t", "int64_t*", "utcTicks"),
                        new NetWireSlot("int16_t", "int16_t*", "offsetMinutes"),
                    }),

                // ONE slot, pointer-shaped, direction-dependent — the shape String already has.
                // 16 bytes in ToByteArray order; NOT two uint64_t, which would make the wire
                // host-endian-dependent and force the managed side to reverse it identically.
                ["System.Guid"] = new(
                    "System.Guid", "Guid", NetWireShape.Conversion,
                    NativeToNet: "BasicLang::net::to_net_guid",
                    NativeFromNet: "BasicLang::net::from_net_guid",
                    CWire: "const uint8_t*", COutWire: "uint8_t*",
                    ConverterForm: NetConverterForm.OutBuffer,
                    NativeTempDecl: "std::uint8_t {0}[16]"),

                // DIRECTIONAL: §6.4's table gives StringBuilder a to-net direction only, so
                // NativeFromNet is null ON PURPOSE and a StringBuilder RESULT must refuse.
                // to_net_stringbuilder's absence of an inverse is pinned by a fast test.
                // ⛔ That null is now the ONLY thing refusing the result direction — it used to
                // ride on IsMultiSlot, and the Conversion result arm must test it explicitly or
                // it emits `dest = (expr);` with no converter at all.
                ["System.Text.StringBuilder"] = new(
                    "System.Text.StringBuilder", "StringBuilder", NetWireShape.Conversion,
                    NativeToNet: "BasicLang::net::to_net_stringbuilder",
                    NativeFromNet: null,
                    CWire: "const char*",
                    ConverterForm: NetConverterForm.OwningTemp,
                    NativeTempDecl: "std::string {0}"),
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

        // MultiSlotConversionPairs lived here. Its only production consumer was the analyzer's
        // multi-slot refusal, which Task 8c-2 deleted when Decimal and DateTimeOffset started
        // lowering — leaving a member with no callers at all. Removed rather than kept "in case",
        // which is how IsSingleSlotValue (just below) became dead without anyone noticing.
        // A caller wanting the set can project WireRows.Values.Where(r => r.IsMultiSlot).

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
