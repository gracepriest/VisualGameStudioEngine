namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// P2a-2 Task 6, spec §6.4: the NATIVE half of the conversion pairs that bridge the six P1
    /// <c>NativeOwned</c> value types to their .NET originals at the boundary. The six are native
    /// values and must never become handles (§6.4), so each needs an explicit wire form — and the
    /// wire forms here are the P1 internal layouts VERBATIM (see the per-pair comments in the
    /// source): a wrong bit position is a silently wrong value, not a crash, which is why
    /// <c>NetConversionPairTests</c> pins hard-coded .NET-computed bit vectors on both sides.
    ///
    /// <para><b>Splice home — surface-keyed, NOT the always-emitted preamble.</b> This block is
    /// carried as its own <c>obj/gen</c> header (<c>blnet_marshal.hpp</c>) in
    /// <see cref="Net.NetProxyEmitter"/>'s §9.1 artifact set, so it exists exactly when a .NET
    /// surface is non-empty. Deliberately NOT spliced into the generated runtime preamble the way
    /// <see cref="CppNetRefRuntime"/> is: NetRef appears in DECLARATION positions of programs
    /// that never call .NET (a declaration-only program must compile shim-free — D-P7), whereas
    /// these converters have call sites only inside proxy invocations, which only exist when the
    /// surface is non-empty. An unconditional splice would churn the always-emitted preamble of
    /// every native program for zero reachable code and re-open the flip's emission-identity
    /// proof; the surface-keyed splice keeps that preamble byte-identical and inherits
    /// <c>NetProxyEmitter</c>'s inertness guarantee (empty surface → no file at all).</para>
    ///
    /// <para><b>Include-order contract.</b> The header references the P1 types
    /// (<c>BasicLang::DateTime</c>/<c>TimeSpan</c>/<c>Decimal</c>/<c>Guid</c>/
    /// <c>DateTimeOffset</c>/<c>StringBuilder</c>) but cannot include them — they are SPLICED
    /// into the generated runtime preamble (<c>CppBclRuntime.BclBody</c> /
    /// <c>CppDecimalRuntime.DecimalBody</c>), not shipped as a standalone header. A consumer TU
    /// (Task 7a's lowered calls; the native tests) must therefore include its runtime preamble —
    /// or the standalone <c>bl_bcltypes.hpp</c>/<c>bl_decimal.hpp</c> — BEFORE this header. It is
    /// NOT included by <c>blnet_proxies.g.hpp</c>/<c>blnet_startup.g.cpp</c>, which stay
    /// self-contained (the startup TU never sees the P1 types).</para>
    ///
    /// <para>Same mechanics as the sibling splices: <c>#ifndef</c>-guarded, carries its own std
    /// includes, every function <c>inline</c> (the header lands in multiple TUs under split
    /// emission), and newlines are '\n' so content hashes are host-independent.</para>
    /// </summary>
    public static class CppNetMarshal
    {
        public const string GuardedSource = @"/* blnet_marshal.hpp — spec §6.4 conversion pairs, native side (P2a-2 Task 6).
   SOURCE OF TRUTH: BasicLang CppNetMarshal.cs — do not edit the emitted copy.
   REQUIRES the P1 native BCL types (BasicLang::DateTime/TimeSpan/Decimal/Guid/
   DateTimeOffset/StringBuilder) to be in scope BEFORE this header: include your
   generated runtime preamble (or bl_bcltypes.hpp + bl_decimal.hpp) first. */
#ifndef BASICLANG_NETMARSHAL_RUNTIME
#define BASICLANG_NETMARSHAL_RUNTIME
#include <cstdint>
#include <cstring>
#include <stdexcept>
#include <string>
namespace BasicLang { namespace net {

/* Direction convention: to_net_* produces the wire value SENT TO .NET; from_net_*
   consumes a wire value RECEIVED FROM .NET. Wire forms are the P1 layouts verbatim —
   the managed mirror (BlnetMarshal.cs, spliced from BlnetShimSources.MarshalCs) must
   agree bit for bit, and NetConversionPairTests pins both sides to the same
   .NET-computed vectors. */

/* ---- DateTime <-> uint64 dateData: ticks in the low 62 bits | kind in the top 2
   (Unspecified=0 / Utc=1 / Local=2 — numerically System.DateTimeKind). This equals
   .NET's ToBinary() dateData for Utc/Unspecified; for Local the wire stays the RAW
   pair (ToBinary's UTC adjustment is serialization-only and is deliberately not
   applied — the managed side reconstructs via the DateTime(long, DateTimeKind)
   constructor, never FromBinary). ---- */
inline std::uint64_t to_net_datetime(const BasicLang::DateTime& v) {
    return v.dateData_;
}
inline BasicLang::DateTime from_net_datetime(std::uint64_t dateData) {
    /* FromTicksAndKind range-checks both fields: kind 3 does not exist in a live
       .NET DateTime and must refuse rather than fabricate a Kind. */
    return BasicLang::DateTime::FromTicksAndKind(
        (std::int64_t)(dateData & BasicLang::DateTime::TicksMask),
        (std::int32_t)(dateData >> 62));
}

/* ---- TimeSpan <-> int64 ticks. ---- */
inline std::int64_t to_net_timespan(const BasicLang::TimeSpan& v) {
    return v.Ticks();
}
inline BasicLang::TimeSpan from_net_timespan(std::int64_t ticks) {
    return BasicLang::TimeSpan(ticks);
}

/* ---- Decimal <-> {lo, mid, hi, flags}: the decimal.GetBits quad. flags = scale
   (0-28) in bits 16-23 | sign in bit 31; every other bit invalid, mirroring
   new decimal(int[]). INBOUND DIVERGENCE (documented at the P1 struct): .NET
   preserves a negative-zero sign flag through GetBits; the native Decimal
   canonicalizes -0 to +0, so the flag drops here (FromParts owns that rule). ---- */
struct NetDecimalWire {
    std::uint32_t lo, mid, hi, flags;
};
inline NetDecimalWire to_net_decimal(const BasicLang::Decimal& v) {
    return { v.lo_, v.mid_, v.hi_, v.flags_ };
}
inline BasicLang::Decimal from_net_decimal(std::uint32_t lo, std::uint32_t mid,
                                           std::uint32_t hi, std::uint32_t flags) {
    if ((flags & 0x7F00FFFFu) != 0u)
        throw std::runtime_error(""Decimal wire flags invalid (bits outside sign|scale)"");
    return BasicLang::Decimal::FromParts(
        lo, mid, hi, (flags & 0x80000000u) != 0u,
        (std::uint8_t)((flags >> 16) & 0xFFu));   /* FromParts rejects scale > 28 */
}
inline BasicLang::Decimal from_net_decimal(const NetDecimalWire& w) {
    return from_net_decimal(w.lo, w.mid, w.hi, w.flags);
}

/* ---- Guid <-> 16 bytes in System.Guid.ToByteArray order: a, b, c little-endian BY
   SHIFTS (host-endianness-independent), then the 8 d_ bytes verbatim. ---- */
inline void to_net_guid(const BasicLang::Guid& v, std::uint8_t out[16]) {
    v.ToByteArray(out);   /* P1 already writes exactly the .NET order */
}
inline BasicLang::Guid from_net_guid(const std::uint8_t in[16]) {
    BasicLang::Guid g;
    g.a_ = (std::int32_t)((std::uint32_t)in[0]
         | ((std::uint32_t)in[1] << 8)
         | ((std::uint32_t)in[2] << 16)
         | ((std::uint32_t)in[3] << 24));
    g.b_ = (std::int16_t)(std::uint16_t)((std::uint16_t)in[4] | ((std::uint16_t)in[5] << 8));
    g.c_ = (std::int16_t)(std::uint16_t)((std::uint16_t)in[6] | ((std::uint16_t)in[7] << 8));
    std::memcpy(g.d_, in + 8, 8);
    return g;
}

/* ---- DateTimeOffset <-> {utcTicks, offsetMinutes}: the P1 fields verbatim (the
   UTC instant + the offset in whole minutes). .NET's constructor takes CLOCK ticks,
   so the MANAGED side adds the offset back; this side stores the pair directly. ---- */
struct NetDateTimeOffsetWire {
    std::int64_t utcTicks;
    std::int16_t offsetMinutes;
};
inline NetDateTimeOffsetWire to_net_datetimeoffset(const BasicLang::DateTimeOffset& v) {
    return { v.utc_.Ticks(), v.offsetMinutes_ };
}
inline BasicLang::DateTimeOffset from_net_datetimeoffset(std::int64_t utcTicks,
                                                         std::int16_t offsetMinutes) {
    /* dto_validate_offset enforces +/-14:00 (whole minutes hold by construction);
       FromTicksAndKind range-checks the UTC instant. */
    (void)BasicLang::bcl_detail::dto_validate_offset(
        (std::int64_t)offsetMinutes * BasicLang::TimeSpan::TicksPerMinute);
    BasicLang::DateTimeOffset r;
    r.utc_ = BasicLang::DateTime::FromTicksAndKind(
        utcTicks, BasicLang::DateTime::KindUnspecified);
    r.offsetMinutes_ = offsetMinutes;
    return r;
}
inline BasicLang::DateTimeOffset from_net_datetimeoffset(const NetDateTimeOffsetWire& w) {
    return from_net_datetimeoffset(w.utcTicks, w.offsetMinutes);
}

/* ---- StringBuilder: crosses BY VALUE as String, ONE direction only (§6.4's table).
   The wire value is the UTF-8 content; the existing String wire rules carry it.
   Nothing converts a .NET StringBuilder back — there is deliberately no
   from_net_stringbuilder. Null-ness of the shared_ptr is the CALL SITE's problem
   (Nothing crosses as the null wire form, not as an empty StringBuilder). ---- */
inline std::string to_net_stringbuilder(const BasicLang::StringBuilder& v) {
    return v.ToString();
}

}} /* namespace BasicLang::net */
#endif /* BASICLANG_NETMARSHAL_RUNTIME */
";
    }
}
