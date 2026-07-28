namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// Single source of truth for the faithful 96-bit Decimal runtime header
    /// <c>bl_decimal.hpp</c> (spec: docs/superpowers/specs/2026-07-27-p1-native-bcl-types-design.md
    /// §10/§11; plan Task 8): .NET System.Decimal semantics from scratch — 96-bit magnitude +
    /// scale 0-28 + sign in the GetBits flag layout, round-half-even EVERYWHERE digits drop
    /// (add/sub overflow reduction, mul reduction, div last digit, Parse beyond capacity,
    /// Round), .NET's division scale rules, Mod as the exact max-scale remainder with the
    /// dividend's sign, and .NET's 15-significant-digit double conversion (VarDecFromR8) with
    /// its exact reverse formula (VarR8FromDec). Every vector pinning this header was verified
    /// against real .NET first (CppDecimalRuntimeTests).
    ///
    /// Structured as (Includes, Body) so Task 9 can splice the include-free
    /// <see cref="DecimalBody"/> into the generated runtime in BOTH emission modes (std headers
    /// are generator-owned there), while the standalone whole-file property
    /// <see cref="DecimalHeader"/> = Includes + Body backs the native vector tests
    /// (<c>CppDecimalRuntimeTests</c>) via <c>CppCompile.CompileAndRun</c> extraFiles.
    /// INDEPENDENT of <c>bl_bcltypes.hpp</c> — Task 9 splices both side by side.
    ///
    /// ODR rule: every out-of-class definition in the header is marked <c>inline</c> — the
    /// header lands in multiple TUs under split emission. Every throw is
    /// <c>std::runtime_error</c> (spec §11: the backend lowers BL catches to that type).
    /// </summary>
    public static class CppDecimalRuntime
    {
        /// <summary>Complete standalone <c>bl_decimal.hpp</c> text (what the native tests compile).</summary>
        public static string DecimalHeader => Includes + Body;

        /// <summary>Banner + <c>#pragma once</c> + std includes (generator-owned when spliced; Task 9).</summary>
        internal static string DecimalIncludes => Includes;

        /// <summary>
        /// Include-free body: opens its own <c>namespace BasicLang</c>; the <c>std::hash</c>
        /// specialization follows the namespace close. This is the piece Task 9 splices.
        /// </summary>
        internal static string DecimalBody => Body;

        private const string Includes = @"/* bl_decimal.hpp — faithful 96-bit .NET System.Decimal engine (P1). Header-only C++20.
   SOURCE OF TRUTH: BasicLang CppDecimalRuntime.cs — do not edit the emitted copy. */
#pragma once
#include <cstdint>
#include <cstdio>
#include <functional>
#include <ostream>
#include <stdexcept>
#include <string>

";

        private const string Body = @"namespace BasicLang {

/* ---- internal limb helpers: 256-bit little-endian magnitudes (uint32 limbs).
   Wide enough for every intermediate: 96x96-bit product = 192 bits; 96-bit
   magnitude x 10^28 rescale = 190 bits. All rounding everywhere is a SINGLE
   round-half-even decision over the whole dropped digit block (.NET's rule). ---- */
namespace dec_detail {

struct U256 {
    uint32_t w[8];
};

inline bool u_iszero(const U256& a) {
    for (int i = 0; i < 8; ++i) if (a.w[i]) return false;
    return true;
}

inline int u_cmp(const U256& a, const U256& b) {
    for (int i = 7; i >= 0; --i)
        if (a.w[i] != b.w[i]) return a.w[i] < b.w[i] ? -1 : 1;
    return 0;
}

inline void u_add(U256& a, const U256& b) {           /* a += b (callers stay in range) */
    uint64_t carry = 0;
    for (int i = 0; i < 8; ++i) {
        uint64_t s = (uint64_t)a.w[i] + b.w[i] + carry;
        a.w[i] = (uint32_t)s;
        carry = s >> 32;
    }
}

inline void u_sub(U256& a, const U256& b) {           /* a -= b (requires a >= b) */
    uint64_t borrow = 0;
    for (int i = 0; i < 8; ++i) {
        uint64_t d = (uint64_t)a.w[i] - b.w[i] - borrow;
        a.w[i] = (uint32_t)d;
        borrow = (d >> 32) & 1u;
    }
}

inline void u_add1(U256& a) {
    for (int i = 0; i < 8; ++i)
        if (++a.w[i] != 0) break;
}

inline void u_add_small(U256& a, uint32_t v) {
    uint64_t carry = v;
    for (int i = 0; i < 8 && carry; ++i) {
        uint64_t cur = (uint64_t)a.w[i] + carry;
        a.w[i] = (uint32_t)cur;
        carry = cur >> 32;
    }
}

inline void u_mul_small(U256& a, uint32_t m) {
    uint64_t carry = 0;
    for (int i = 0; i < 8; ++i) {
        uint64_t p = (uint64_t)a.w[i] * m + carry;
        a.w[i] = (uint32_t)p;
        carry = p >> 32;
    }
}

inline void u_scale10(U256& a, int32_t k) { while (k-- > 0) u_mul_small(a, 10u); }

inline uint32_t u_div10(U256& a) {                    /* a /= 10, returns the dropped digit */
    uint64_t rem = 0;
    for (int i = 7; i >= 0; --i) {
        uint64_t cur = (rem << 32) | a.w[i];
        a.w[i] = (uint32_t)(cur / 10u);
        rem = cur % 10u;
    }
    return (uint32_t)rem;
}

inline bool u_fits96(const U256& a) {
    return (a.w[3] | a.w[4] | a.w[5] | a.w[6] | a.w[7]) == 0;
}

inline int u_highbit(const U256& a) {                 /* index of highest set bit, -1 if zero */
    for (int i = 7; i >= 0; --i) {
        if (a.w[i]) {
            uint32_t v = a.w[i];
            int b = 31;
            while (!(v & 0x80000000u)) { v <<= 1; --b; }
            return i * 32 + b;
        }
    }
    return -1;
}

inline bool u_bit(const U256& a, int i) { return ((a.w[i >> 5] >> (i & 31)) & 1u) != 0; }

inline void u_shl1(U256& a) {
    uint32_t carry = 0;
    for (int i = 0; i < 8; ++i) {
        uint32_t nc = a.w[i] >> 31;
        a.w[i] = (a.w[i] << 1) | carry;
        carry = nc;
    }
}

/* binary long division: q = n / d, r = n % d (d must be nonzero) */
inline void u_divmod(const U256& n, const U256& d, U256& q, U256& r) {
    q = U256{};
    r = U256{};
    for (int i = u_highbit(n); i >= 0; --i) {
        u_shl1(r);
        r.w[0] |= (uint32_t)u_bit(n, i);
        if (u_cmp(r, d) >= 0) {
            u_sub(r, d);
            q.w[i >> 5] |= (1u << (i & 31));
        }
    }
}

inline U256 u_from96(uint32_t lo, uint32_t mid, uint32_t hi) {
    U256 r{};
    r.w[0] = lo; r.w[1] = mid; r.w[2] = hi;
    return r;
}

/* Reduce m until it fits 96 bits AND scale <= 28, dropping decimal digits with one
   round-half-even decision (last = most significant dropped digit, sticky = any other
   dropped digit nonzero). Throws when the value cannot fit at scale 0. */
inline void u_reduce(U256& m, int32_t& scale) {
    int32_t last = -1;
    bool sticky = false;
    while (scale > 28 || !u_fits96(m)) {
        if (scale == 0) throw std::runtime_error(""Decimal overflow"");
        if (last > 0) sticky = true;
        last = (int32_t)u_div10(m);
        --scale;
    }
    if (last < 0) return;                             /* nothing dropped */
    if (last > 5 || (last == 5 && (sticky || (m.w[0] & 1u)))) {
        u_add1(m);
        if (!u_fits96(m)) {                           /* rounded up to exactly 2^96 (…950336) */
            if (scale == 0) throw std::runtime_error(""Decimal overflow"");
            u_div10(m);                               /* remainder is 6 -> always rounds up */
            u_add1(m);
            --scale;
        }
    }
}

} /* namespace dec_detail */

/* ================= Decimal =================
   .NET System.Decimal layout: 96-bit unsigned magnitude {lo_, mid_, hi_}; flags_ holds
   the scale (0-28) in bits 16-23 and the sign in bit 31; all other flag bits are zero.
   GetBits order == {lo_, mid_, hi_, flags_}. Negative zero is canonicalized to +0 —
   DOCUMENTED DIVERGENCE: real .NET PRESERVES a -0 sign flag through GetBits, so Task 9
   must not round-trip .NET -0 bits via FromParts expecting the sign to survive. */
struct Decimal {
    uint32_t lo_, mid_, hi_, flags_;

    constexpr Decimal() : lo_(0), mid_(0), hi_(0), flags_(0) {}
    Decimal(int32_t v);
    Decimal(uint32_t v);
    Decimal(int64_t v);
    Decimal(uint64_t v);
    explicit Decimal(double v);                       /* .NET's 15-significant-digit rule */

    static Decimal FromParts(uint32_t lo, uint32_t mid, uint32_t hi, bool neg, uint8_t scale);
    static Decimal Zero() { return Decimal(); }
    static Decimal One() { return FromParts(1u, 0u, 0u, false, 0); }
    static Decimal MinValue() { return FromParts(0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, true, 0); }
    static Decimal MaxValue() { return FromParts(0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, false, 0); }

    int32_t Scale() const { return (int32_t)((flags_ >> 16) & 0xFFu); }
    bool IsNegative() const { return (flags_ & 0x80000000u) != 0; }
    bool IsZeroMag() const { return (lo_ | mid_ | hi_) == 0; }

    static int32_t Compare(const Decimal& a, const Decimal& b);
    int32_t CompareTo(const Decimal& other) const { return Compare(*this, other); }

    static Decimal Parse(const std::string& s);
    std::string ToString() const;                     /* scale-preserving: trailing zeros KEPT */

    static Decimal Round(const Decimal& d) { return Round(d, 0); }
    static Decimal Round(const Decimal& d, int32_t digits);   /* banker's rounding */
    static Decimal Truncate(const Decimal& d);
    static Decimal Floor(const Decimal& d);
    static Decimal Ceiling(const Decimal& d);

    double ToDouble() const;                          /* .NET VarR8FromDec formula */

    Decimal operator-() const;
    Decimal& operator++();
    Decimal operator++(int);
    Decimal& operator--();
    Decimal operator--(int);
    Decimal& operator+=(const Decimal& o);
    Decimal& operator-=(const Decimal& o);
    Decimal& operator*=(const Decimal& o);
    Decimal& operator/=(const Decimal& o);
    Decimal& operator%=(const Decimal& o);
};

namespace dec_detail {

/* Assemble a Decimal from a reduced magnitude (must fit 96 bits, scale 0-28).
   Zero keeps its scale (1.50 - 1.50 prints ""0.00"") but never carries a sign. */
inline Decimal make_decimal(const U256& m, int32_t scale, bool neg) {
    Decimal d;
    d.lo_ = m.w[0]; d.mid_ = m.w[1]; d.hi_ = m.w[2];
    bool zero = (m.w[0] | m.w[1] | m.w[2]) == 0;
    d.flags_ = ((uint32_t)scale << 16) | ((neg && !zero) ? 0x80000000u : 0u);
    return d;
}

/* Shared add/sub core: align scales to max(s1, s2), combine magnitudes with sign
   logic, reduce a same-sign carry-out with round-half-even (throws at scale 0). */
inline Decimal dec_addsub(const Decimal& a, const Decimal& b, bool negateB) {
    bool sa = a.IsNegative();
    bool sb = b.IsNegative() != negateB;
    int32_t ca = a.Scale(), cb = b.Scale();
    int32_t s = ca > cb ? ca : cb;
    U256 A = u_from96(a.lo_, a.mid_, a.hi_);
    u_scale10(A, s - ca);
    U256 B = u_from96(b.lo_, b.mid_, b.hi_);
    u_scale10(B, s - cb);
    if (sa == sb) {
        u_add(A, B);
        u_reduce(A, s);
        return make_decimal(A, s, sa);
    }
    int c = u_cmp(A, B);
    if (c == 0) return make_decimal(U256{}, s, false);
    /* the scale-ALIGNED difference can still exceed 96 bits (MaxValue - 0.5 aligns the
       larger side x10) — reduce on BOTH paths too (no-op when it already fits) */
    if (c > 0) { u_sub(A, B); u_reduce(A, s); return make_decimal(A, s, sa); }
    u_sub(B, A);
    u_reduce(B, s);
    return make_decimal(B, s, sb);
}

} /* namespace dec_detail */

inline Decimal operator+(const Decimal& a, const Decimal& b) { return dec_detail::dec_addsub(a, b, false); }
inline Decimal operator-(const Decimal& a, const Decimal& b) { return dec_detail::dec_addsub(a, b, true); }

inline Decimal operator*(const Decimal& x, const Decimal& y) {
    using namespace dec_detail;
    const uint32_t xa[3] = { x.lo_, x.mid_, x.hi_ };
    const uint32_t yb[3] = { y.lo_, y.mid_, y.hi_ };
    U256 p{};
    for (int i = 0; i < 3; ++i) {                     /* schoolbook 96x96 -> 192 */
        uint64_t carry = 0;
        for (int j = 0; j < 3; ++j) {
            uint64_t cur = (uint64_t)xa[i] * yb[j] + p.w[i + j] + carry;
            p.w[i + j] = (uint32_t)cur;
            carry = cur >> 32;
        }
        int k = i + 3;
        while (carry) {
            uint64_t cur = (uint64_t)p.w[k] + carry;
            p.w[k] = (uint32_t)cur;
            carry = cur >> 32;
            ++k;
        }
    }
    int32_t s = x.Scale() + y.Scale();                /* then reduce to <=28 / 96 bits */
    u_reduce(p, s);
    return make_decimal(p, s, x.IsNegative() != y.IsNegative());
}

inline Decimal operator/(const Decimal& x, const Decimal& y) {
    using namespace dec_detail;
    if (y.IsZeroMag()) throw std::runtime_error(""Decimal division by zero"");
    bool neg = x.IsNegative() != y.IsNegative();
    int32_t scale = x.Scale() - y.Scale();
    U256 A = u_from96(x.lo_, x.mid_, x.hi_);
    const U256 B = u_from96(y.lo_, y.mid_, y.hi_);
    if (scale < 0) {                                  /* larger divisor scale: pre-scale the dividend */
        u_scale10(A, -scale);
        scale = 0;
    }
    U256 Q, R;
    u_divmod(A, B, Q, R);
    if (!u_fits96(Q)) throw std::runtime_error(""Decimal overflow"");
    if (u_iszero(R)) return make_decimal(Q, scale, neg);  /* exact: keep scale sa-sb (10.00/4 = ""2.50"") */
    /* inexact: extend one decimal digit at a time while the quotient keeps fitting */
    while (scale < 28) {
        U256 Q10 = Q;
        u_mul_small(Q10, 10u);
        U256 R10 = R;
        u_mul_small(R10, 10u);
        uint32_t d = 0;
        while (u_cmp(R10, B) >= 0) { u_sub(R10, B); ++d; }   /* digit 0-9 since R < B */
        u_add_small(Q10, d);
        if (!u_fits96(Q10)) break;
        Q = Q10;
        R = R10;
        ++scale;
        if (u_iszero(R)) return make_decimal(Q, scale, neg);
    }
    /* out of capacity: round-half-even on the next digit */
    U256 R10 = R;
    u_mul_small(R10, 10u);
    uint32_t d = 0;
    while (u_cmp(R10, B) >= 0) { u_sub(R10, B); ++d; }
    if (d > 5 || (d == 5 && (!u_iszero(R10) || (Q.w[0] & 1u)))) {
        u_add1(Q);
        if (!u_fits96(Q)) {                           /* 2^96: shed one digit (remainder 6 rounds up) */
            if (scale == 0) throw std::runtime_error(""Decimal overflow"");
            u_div10(Q);
            u_add1(Q);
            --scale;
        }
    }
    if (u_iszero(Q)) return Decimal();                /* .NET: inexact quotient rounding to zero -> plain 0 */
    return make_decimal(Q, scale, neg);
}

inline Decimal operator%(const Decimal& x, const Decimal& y) {
    using namespace dec_detail;
    if (y.IsZeroMag()) throw std::runtime_error(""Decimal division by zero"");
    /* exact remainder at scale max(sa, sb): a - Truncate(a/b)*b with the dividend's sign.
       Always representable: r <= |a| bounds it when msc == sa, r < |b| when msc == sb. */
    int32_t ms = x.Scale() > y.Scale() ? x.Scale() : y.Scale();
    U256 A = u_from96(x.lo_, x.mid_, x.hi_);
    u_scale10(A, ms - x.Scale());
    U256 B = u_from96(y.lo_, y.mid_, y.hi_);
    u_scale10(B, ms - y.Scale());
    U256 Q, R;
    u_divmod(A, B, Q, R);
    return make_decimal(R, ms, x.IsNegative());
}

inline bool operator==(const Decimal& a, const Decimal& b) { return Decimal::Compare(a, b) == 0; }
inline bool operator!=(const Decimal& a, const Decimal& b) { return Decimal::Compare(a, b) != 0; }
inline bool operator<(const Decimal& a, const Decimal& b) { return Decimal::Compare(a, b) < 0; }
inline bool operator<=(const Decimal& a, const Decimal& b) { return Decimal::Compare(a, b) <= 0; }
inline bool operator>(const Decimal& a, const Decimal& b) { return Decimal::Compare(a, b) > 0; }
inline bool operator>=(const Decimal& a, const Decimal& b) { return Decimal::Compare(a, b) >= 0; }

/* ================= member/static bodies ================= */

inline Decimal::Decimal(int32_t v) : lo_(0), mid_(0), hi_(0), flags_(0) {
    uint64_t m = (uint64_t)(int64_t)v;
    if (v < 0) { m = ~m + 1u; flags_ = 0x80000000u; }
    lo_ = (uint32_t)m;
    mid_ = (uint32_t)(m >> 32);
}

inline Decimal::Decimal(uint32_t v) : lo_(v), mid_(0), hi_(0), flags_(0) {}

inline Decimal::Decimal(int64_t v) : lo_(0), mid_(0), hi_(0), flags_(0) {
    uint64_t m = (uint64_t)v;
    if (v < 0) { m = ~m + 1u; flags_ = 0x80000000u; }
    lo_ = (uint32_t)m;
    mid_ = (uint32_t)(m >> 32);
}

inline Decimal::Decimal(uint64_t v) : lo_((uint32_t)v), mid_((uint32_t)(v >> 32)), hi_(0), flags_(0) {}

inline Decimal Decimal::FromParts(uint32_t lo, uint32_t mid, uint32_t hi, bool neg, uint8_t scale) {
    if (scale > 28) throw std::runtime_error(""Decimal scale out of range"");
    dec_detail::U256 m = dec_detail::u_from96(lo, mid, hi);
    return dec_detail::make_decimal(m, (int32_t)scale, neg);
}

inline int32_t Decimal::Compare(const Decimal& a, const Decimal& b) {
    using namespace dec_detail;
    bool az = a.IsZeroMag(), bz = b.IsZeroMag();
    if (az || bz) {
        if (az && bz) return 0;
        if (az) return b.IsNegative() ? 1 : -1;
        return a.IsNegative() ? -1 : 1;
    }
    if (a.IsNegative() != b.IsNegative()) return a.IsNegative() ? -1 : 1;
    U256 A = u_from96(a.lo_, a.mid_, a.hi_);
    U256 B = u_from96(b.lo_, b.mid_, b.hi_);
    int32_t ca = a.Scale(), cb = b.Scale();
    if (ca < cb) u_scale10(A, cb - ca);
    else u_scale10(B, ca - cb);
    int c = u_cmp(A, B);
    return a.IsNegative() ? -c : c;
}

inline Decimal Decimal::Parse(const std::string& str) {
    using namespace dec_detail;
    size_t b = 0, e = str.size();
    while (b < e && (str[b] == ' ' || str[b] == '\t' || str[b] == '\r' || str[b] == '\n')) ++b;
    while (e > b && (str[e - 1] == ' ' || str[e - 1] == '\t' || str[e - 1] == '\r' || str[e - 1] == '\n')) --e;
    if (b >= e) throw std::runtime_error(""Decimal Parse: input string was not in a correct format"");
    bool neg = false;
    if (str[b] == '+' || str[b] == '-') { neg = str[b] == '-'; ++b; }
    U256 m{};
    int32_t scale = 0;
    bool anyDigit = false, seenDot = false, sticky = false;
    int32_t last = -1;                                /* first dropped fractional digit */
    for (size_t i = b; i < e; ++i) {
        char c = str[i];
        if (c == '.') {
            if (seenDot) throw std::runtime_error(""Decimal Parse: input string was not in a correct format"");
            seenDot = true;
            continue;
        }
        if (c < '0' || c > '9') throw std::runtime_error(""Decimal Parse: input string was not in a correct format"");
        anyDigit = true;
        uint32_t d = (uint32_t)(c - '0');
        if (!seenDot) {
            u_mul_small(m, 10u);
            u_add_small(m, d);
            if (!u_fits96(m)) throw std::runtime_error(""Decimal overflow"");
        } else if (last < 0 && scale < 28) {
            U256 t = m;
            u_mul_small(t, 10u);
            u_add_small(t, d);
            if (u_fits96(t)) { m = t; ++scale; continue; }
            last = (int32_t)d;                        /* digit would not fit: starts the dropped block */
        } else if (last < 0) {
            last = (int32_t)d;                        /* scale cap reached */
        } else if (d != 0) {
            sticky = true;
        }
    }
    if (!anyDigit) throw std::runtime_error(""Decimal Parse: input string was not in a correct format"");
    if (last >= 0 && (last > 5 || (last == 5 && (sticky || (m.w[0] & 1u))))) {
        u_add1(m);
        if (!u_fits96(m)) {                           /* 2^96: shed one digit (remainder 6 rounds up) */
            if (scale == 0) throw std::runtime_error(""Decimal overflow"");
            u_div10(m);
            u_add1(m);
            --scale;
        }
    }
    return make_decimal(m, scale, neg);
}

inline std::string Decimal::ToString() const {
    uint32_t w[3] = { lo_, mid_, hi_ };
    char digits[30];
    int n = 0;
    do {                                              /* repeated div-10 digit extraction */
        uint64_t rem = 0;
        for (int i = 2; i >= 0; --i) {
            uint64_t cur = (rem << 32) | w[i];
            w[i] = (uint32_t)(cur / 10u);
            rem = cur % 10u;
        }
        digits[n++] = (char)('0' + (int)rem);
    } while (w[0] | w[1] | w[2]);
    int32_t s = Scale();
    std::string out;
    if (IsNegative()) out += '-';
    if (s >= n) {
        out += ""0."";
        for (int i = 0; i < s - n; ++i) out += '0';
        for (int i = n - 1; i >= 0; --i) out += digits[i];
    } else {
        for (int i = n - 1; i >= 0; --i) {
            out += digits[i];
            if (i == s && s != 0) out += '.';
        }
    }
    return out;
}

inline Decimal Decimal::Round(const Decimal& d, int32_t digits) {
    using namespace dec_detail;
    if (digits < 0 || digits > 28) throw std::runtime_error(""Decimal Round: digits out of range"");
    int32_t s = d.Scale();
    if (s <= digits) return d;
    U256 m = u_from96(d.lo_, d.mid_, d.hi_);
    int32_t drop = s - digits;
    int32_t last = -1;
    bool sticky = false;
    while (drop-- > 0) {
        if (last > 0) sticky = true;
        last = (int32_t)u_div10(m);
    }
    if (last > 5 || (last == 5 && (sticky || (m.w[0] & 1u))))
        u_add1(m);                                    /* cannot overflow: at least one digit dropped */
    return make_decimal(m, digits, d.IsNegative());
}

inline Decimal Decimal::Truncate(const Decimal& d) {
    using namespace dec_detail;
    int32_t s = d.Scale();
    if (s == 0) return d;
    U256 m = u_from96(d.lo_, d.mid_, d.hi_);
    while (s-- > 0) u_div10(m);
    return make_decimal(m, 0, d.IsNegative());
}

inline Decimal Decimal::Floor(const Decimal& d) {
    using namespace dec_detail;
    int32_t s = d.Scale();
    if (s == 0) return d;
    U256 m = u_from96(d.lo_, d.mid_, d.hi_);
    bool dropped = false;
    while (s-- > 0) dropped = (u_div10(m) != 0) || dropped;
    if (d.IsNegative() && dropped) u_add1(m);         /* toward negative infinity */
    return make_decimal(m, 0, d.IsNegative());
}

inline Decimal Decimal::Ceiling(const Decimal& d) {
    using namespace dec_detail;
    int32_t s = d.Scale();
    if (s == 0) return d;
    U256 m = u_from96(d.lo_, d.mid_, d.hi_);
    bool dropped = false;
    while (s-- > 0) dropped = (u_div10(m) != 0) || dropped;
    if (!d.IsNegative() && dropped) u_add1(m);        /* toward positive infinity */
    return make_decimal(m, 0, d.IsNegative());
}

inline Decimal::Decimal(double v) : lo_(0), mid_(0), hi_(0), flags_(0) {
    using namespace dec_detail;
    if (!(v - v == 0.0)) throw std::runtime_error(""Decimal overflow"");        /* NaN / infinity */
    bool neg = false;
    double av = v;
    if (av < 0.0) { neg = true; av = -av; }
    if (av >= 79228162514264337593543950336.0)                                /* 2^96, .NET's exponent guard */
        throw std::runtime_error(""Decimal overflow"");
    if (av == 0.0) return;                                                    /* +0 scale 0 (also -0.0) */
    /* .NET VarDecFromR8: round to 15 significant digits, then strip trailing zeros.
       %.14e gives the correctly rounded 15-digit form ""d.ddddddddddddddde+XX"". */
    char buf[40];
    std::snprintf(buf, sizeof buf, ""%.14e"", av);
    uint64_t M = 0;
    int nd = 0;
    size_t i = 0;
    for (; buf[i] != '\0' && buf[i] != 'e' && buf[i] != 'E'; ++i) {
        if (buf[i] == '.') continue;
        M = M * 10u + (uint64_t)(buf[i] - '0');
        ++nd;
    }
    int expo = 0;
    bool eneg = false;
    if (buf[i] == 'e' || buf[i] == 'E') {
        ++i;
        if (buf[i] == '+') ++i;
        else if (buf[i] == '-') { eneg = true; ++i; }
        for (; buf[i] != '\0'; ++i) expo = expo * 10 + (buf[i] - '0');
        if (eneg) expo = -expo;
    }
    if (M == 0) return;
    int32_t p = expo - (nd - 1);                      /* value = M * 10^p */
    if (p >= 0) {
        U256 m{};
        m.w[0] = (uint32_t)M;
        m.w[1] = (uint32_t)(M >> 32);
        u_scale10(m, p);
        if (!u_fits96(m)) throw std::runtime_error(""Decimal overflow"");
        *this = make_decimal(m, 0, neg);
        return;
    }
    int32_t scale = -p;
    while (scale > 0 && (M % 10u) == 0) { M /= 10u; --scale; }
    if (scale > 28) {
        int32_t drop = scale - 28;
        int32_t lastd = -1;
        bool sticky = false;
        while (drop-- > 0) {
            if (lastd > 0) sticky = true;
            lastd = (int32_t)(M % 10u);
            M /= 10u;
        }
        if (lastd > 5 || (lastd == 5 && (sticky || (M & 1u)))) ++M;           /* M < 10^15: no overflow */
        scale = 28;
        if (M == 0) return;                                                   /* rounded away: plain 0 */
        while (scale > 0 && (M % 10u) == 0) { M /= 10u; --scale; }            /* .NET strips after rounding */
    }
    U256 m{};
    m.w[0] = (uint32_t)M;
    m.w[1] = (uint32_t)(M >> 32);
    *this = make_decimal(m, scale, neg);
}

inline double Decimal::ToDouble() const {
    /* .NET VarR8FromDec, verbatim: (low64 + high*2^64) / 10^scale in double arithmetic */
    static const double p10[29] = {
        1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14,
        1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22, 1e23, 1e24, 1e25, 1e26, 1e27, 1e28
    };
    uint64_t low64 = ((uint64_t)mid_ << 32) | lo_;
    double d = ((double)low64 + (double)hi_ * 18446744073709551616.0) / p10[Scale()];
    return IsNegative() ? -d : d;
}

inline Decimal Decimal::operator-() const {
    Decimal r = *this;
    if (!r.IsZeroMag()) r.flags_ ^= 0x80000000u;      /* zero stays canonical +0 */
    return r;
}

inline Decimal& Decimal::operator++() { *this = *this + One(); return *this; }
inline Decimal Decimal::operator++(int) { Decimal t = *this; *this = *this + One(); return t; }
inline Decimal& Decimal::operator--() { *this = *this - One(); return *this; }
inline Decimal Decimal::operator--(int) { Decimal t = *this; *this = *this - One(); return t; }
inline Decimal& Decimal::operator+=(const Decimal& o) { *this = *this + o; return *this; }
inline Decimal& Decimal::operator-=(const Decimal& o) { *this = *this - o; return *this; }
inline Decimal& Decimal::operator*=(const Decimal& o) { *this = *this * o; return *this; }
inline Decimal& Decimal::operator/=(const Decimal& o) { *this = *this / o; return *this; }
inline Decimal& Decimal::operator%=(const Decimal& o) { *this = *this % o; return *this; }

inline std::ostream& operator<<(std::ostream& os, const Decimal& v) { return os << v.ToString(); }

} /* namespace BasicLang */

template<> struct std::hash<BasicLang::Decimal> {
    size_t operator()(const BasicLang::Decimal& v) const noexcept {
        /* hash the SCALE-NORMALIZED form: divide out trailing zeros first, so 1.0 and
           1.00 (equal values) hash equal; zero is fully canonical regardless of scale. */
        uint32_t w[3] = { v.lo_, v.mid_, v.hi_ };
        uint32_t scale = (v.flags_ >> 16) & 0xFFu;
        uint32_t sign = (w[0] | w[1] | w[2]) != 0 ? (v.flags_ >> 31) : 0u;
        while (scale > 0) {
            uint64_t rem = 0;
            uint32_t t[3];
            for (int i = 2; i >= 0; --i) {
                uint64_t cur = (rem << 32) | w[i];
                t[i] = (uint32_t)(cur / 10u);
                rem = cur % 10u;
            }
            if (rem != 0) break;
            w[0] = t[0]; w[1] = t[1]; w[2] = t[2];
            --scale;
        }
        if ((w[0] | w[1] | w[2]) == 0) scale = 0;
        uint64_t h = 1469598103934665603ULL;          /* FNV-1a over the normalized parts */
        const uint32_t parts[4] = { w[0], w[1], w[2], scale | (sign << 31) };
        for (uint32_t x : parts)
            for (int i = 0; i < 4; ++i) { h ^= (x >> (i * 8)) & 0xFFu; h *= 1099511628211ULL; }
        return (size_t)h;
    }
};
";
    }
}
