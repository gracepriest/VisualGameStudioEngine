namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// Single source of truth for the native BCL value-type runtime header
    /// <c>bl_bcltypes.hpp</c> (spec: docs/superpowers/specs/2026-07-27-p1-native-bcl-types-design.md
    /// §3/§9): DateTime + TimeSpan (P1 Task 6; Guid/DateTimeOffset/StringBuilder extend the
    /// same file in Task 7).
    ///
    /// Structured as (Includes, Body) so Task 9 can splice the include-free
    /// <see cref="BclBody"/> into the generated runtime in BOTH emission modes (std headers
    /// are generator-owned there), while the standalone whole-file property
    /// <see cref="BclHeader"/> = Includes + Body backs the native vector tests
    /// (<c>CppBclRuntimeTests</c>) via <c>CppCompile.CompileAndRun</c> extraFiles.
    ///
    /// ODR rule: every out-of-class definition in the header is marked <c>inline</c> — the
    /// header lands in multiple TUs under split emission.
    /// </summary>
    public static class CppBclRuntime
    {
        /// <summary>Complete standalone <c>bl_bcltypes.hpp</c> text (what the native tests compile).</summary>
        public static string BclHeader => Includes + Body;

        /// <summary>Banner + <c>#pragma once</c> + std includes (generator-owned when spliced; Task 9).</summary>
        internal static string BclIncludes => Includes;

        /// <summary>
        /// Include-free body: opens its own <c>namespace BasicLang</c>; the <c>std::hash</c>
        /// specializations follow the namespace close. This is the piece Task 9 splices.
        /// </summary>
        internal static string BclBody => Body;

        private const string Includes = @"/* bl_bcltypes.hpp — native BCL value types (P1). Header-only C++20.
   SOURCE OF TRUTH: BasicLang CppBclRuntime.cs — do not edit the emitted copy. */
#pragma once
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <ctime>
#include <chrono>
#include <functional>
#include <memory>
#include <ostream>
#include <stdexcept>
#include <string>

";

        private const string Body = @"namespace BasicLang {

/* ---- internal helpers: civil-date math (Howard Hinnant's public-domain algorithms,
   days relative to 1970-01-01) + OS time wrappers + strict digit parsing ---- */
namespace bcl_detail {

inline int64_t days_from_civil(int32_t y, int32_t m, int32_t d) {
    y -= m <= 2;
    const int64_t era = (y >= 0 ? y : y - 399) / 400;
    const uint32_t yoe = (uint32_t)(y - era * 400);                                    /* [0, 399] */
    const uint32_t doy = (153u * (uint32_t)(m + (m > 2 ? -3 : 9)) + 2u) / 5u + (uint32_t)d - 1u; /* [0, 365] */
    const uint32_t doe = yoe * 365u + yoe / 4u - yoe / 100u + doy;                     /* [0, 146096] */
    return era * 146097 + (int64_t)doe - 719468;
}

inline void civil_from_days(int64_t z, int32_t& y, int32_t& m, int32_t& d) {
    z += 719468;
    const int64_t era = (z >= 0 ? z : z - 146096) / 146097;
    const uint32_t doe = (uint32_t)(z - era * 146097);                                 /* [0, 146096] */
    const uint32_t yoe = (doe - doe / 1460u + doe / 36524u - doe / 146096u) / 365u;    /* [0, 399] */
    const int64_t yy = (int64_t)yoe + era * 400;
    const uint32_t doy = doe - (365u * yoe + yoe / 4u - yoe / 100u);                   /* [0, 365] */
    const uint32_t mp = (5u * doy + 2u) / 153u;                                        /* [0, 11] */
    d = (int32_t)(doy - (153u * mp + 2u) / 5u + 1u);                                   /* [1, 31] */
    m = (int32_t)(mp < 10 ? mp + 3 : mp - 9);                                          /* [1, 12] */
    y = (int32_t)(yy + (m <= 2));
}

/* days from 0001-01-01 to 1970-01-01, and the Unix epoch in ticks */
constexpr int64_t DaysTo1970 = 719162;
constexpr int64_t UnixEpochTicks = 621355968000000000LL;

inline bool os_local_time(std::time_t t, std::tm& out) {
#if defined(_MSC_VER)
    return localtime_s(&out, &t) == 0;
#else
    std::tm* p = std::localtime(&t);
    if (!p) return false;
    out = *p;
    return true;
#endif
}

inline bool os_gm_time(std::time_t t, std::tm& out) {
#if defined(_MSC_VER)
    return gmtime_s(&out, &t) == 0;
#else
    std::tm* p = std::gmtime(&t);
    if (!p) return false;
    out = *p;
    return true;
#endif
}

/* consume [minD..maxD] decimal digits at pos; false if fewer than minD present */
inline bool parse_digits(const std::string& s, size_t& pos, int minD, int maxD, int64_t& out) {
    int n = 0;
    int64_t v = 0;
    while (pos < s.size() && s[pos] >= '0' && s[pos] <= '9' && n < maxD) {
        v = v * 10 + (s[pos] - '0');
        ++pos;
        ++n;
    }
    if (n < minD) return false;
    out = v;
    return true;
}

inline bool expect_char(const std::string& s, size_t& pos, char c) {
    if (pos < s.size() && s[pos] == c) { ++pos; return true; }
    return false;
}

} /* namespace bcl_detail */

/* ---- TimeSpan: one int64 ticks (100ns). Spec §3. ---- */
struct TimeSpan {
    int64_t ticks_ = 0;
    static constexpr int64_t TicksPerMillisecond = 10'000;
    static constexpr int64_t TicksPerSecond = 10'000'000;
    static constexpr int64_t TicksPerMinute = TicksPerSecond * 60;
    static constexpr int64_t TicksPerHour   = TicksPerMinute * 60;
    static constexpr int64_t TicksPerDay    = TicksPerHour * 24;

    TimeSpan() = default;
    explicit TimeSpan(int64_t ticks) : ticks_(ticks) {}
    TimeSpan(int32_t h, int32_t m, int32_t s) : ticks_(((int64_t)h*3600 + (int64_t)m*60 + s) * TicksPerSecond) {}
    TimeSpan(int32_t d, int32_t h, int32_t m, int32_t s)
        : ticks_((int64_t)d*TicksPerDay + ((int64_t)h*3600 + (int64_t)m*60 + s) * TicksPerSecond) {}

    static TimeSpan FromTicks(int64_t t) { return TimeSpan(t); }
    /* double-based factories round to the NEAREST MILLISECOND (.NET rule, spec §5) */
    static TimeSpan FromDays(double v)         { return Interval(v, TicksPerDay); }
    static TimeSpan FromHours(double v)        { return Interval(v, TicksPerHour); }
    static TimeSpan FromMinutes(double v)      { return Interval(v, TicksPerMinute); }
    static TimeSpan FromSeconds(double v)      { return Interval(v, TicksPerSecond); }
    static TimeSpan FromMilliseconds(double v) { return Interval(v, TicksPerMillisecond); }
    static TimeSpan Zero() { return TimeSpan(0); }
    static TimeSpan MinValue() { return TimeSpan(INT64_MIN); }
    static TimeSpan MaxValue() { return TimeSpan(INT64_MAX); }
    static TimeSpan Parse(const std::string& s);   /* ""c"" format: [-][d.]hh:mm:ss[.fffffff] */

    int64_t Ticks() const { return ticks_; }
    int32_t Days() const    { return (int32_t)(ticks_ / TicksPerDay); }
    int32_t Hours() const   { return (int32_t)((ticks_ / TicksPerHour) % 24); }
    int32_t Minutes() const { return (int32_t)((ticks_ / TicksPerMinute) % 60); }
    int32_t Seconds() const { return (int32_t)((ticks_ / TicksPerSecond) % 60); }
    int32_t Milliseconds() const { return (int32_t)((ticks_ / TicksPerMillisecond) % 1000); }
    double TotalDays() const    { return (double)ticks_ / TicksPerDay; }
    double TotalHours() const   { return (double)ticks_ / TicksPerHour; }
    double TotalMinutes() const { return (double)ticks_ / TicksPerMinute; }
    double TotalSeconds() const { return (double)ticks_ / TicksPerSecond; }
    double TotalMilliseconds() const { return (double)ticks_ / TicksPerMillisecond; }

    TimeSpan Add(const TimeSpan& o) const { return CheckedAdd(ticks_, o.ticks_); }
    /* NOT CheckedAdd(ticks_, -o.ticks_): negating INT64_MIN is UB and gets the
       MinValue edge wrong; checked subtraction in unsigned arithmetic instead */
    TimeSpan Subtract(const TimeSpan& o) const {
        int64_t r = (int64_t)((uint64_t)ticks_ - (uint64_t)o.ticks_);
        if (((ticks_ ^ o.ticks_) & (ticks_ ^ r)) < 0) throw std::runtime_error(""TimeSpan overflow in subtraction"");
        return TimeSpan(r);
    }
    TimeSpan Negate() const { if (ticks_ == INT64_MIN) throw std::runtime_error(""TimeSpan overflow: negating MinValue""); return TimeSpan(-ticks_); }
    TimeSpan Duration() const { return ticks_ < 0 ? Negate() : *this; }
    int32_t CompareTo(const TimeSpan& o) const { return ticks_ < o.ticks_ ? -1 : (ticks_ > o.ticks_ ? 1 : 0); }
    std::string ToString() const;                  /* ""c"" invariant format */

    TimeSpan operator+(const TimeSpan& o) const { return Add(o); }
    TimeSpan operator-(const TimeSpan& o) const { return Subtract(o); }
    TimeSpan operator-() const { return Negate(); }
    bool operator==(const TimeSpan& o) const = default;
    auto operator<=>(const TimeSpan& o) const = default;

private:
    static TimeSpan Interval(double v, int64_t scaleTicks);   /* v*scale rounded to nearest ms; NaN/overflow throw */
    static TimeSpan CheckedAdd(int64_t a, int64_t b);         /* overflow -> throw std::runtime_error */
};

/* ---- DateTime: uint64 = ticks (low 62) | kind (top 2). Spec §3. ---- */
struct DateTime {
    uint64_t dateData_ = 0;
    static constexpr uint64_t TicksMask = 0x3FFFFFFFFFFFFFFFULL;
    static constexpr int32_t KindUnspecified = 0, KindUtc = 1, KindLocal = 2;
    static constexpr int64_t MaxTicks = 3155378975999999999LL;   /* 9999-12-31T23:59:59.9999999 */

    DateTime() = default;
    DateTime(int32_t y, int32_t mo, int32_t d) { Init(y, mo, d, 0, 0, 0); }
    DateTime(int32_t y, int32_t mo, int32_t d, int32_t h, int32_t mi, int32_t s) { Init(y, mo, d, h, mi, s); }
    static DateTime FromTicksAndKind(int64_t ticks, int32_t kind);   /* range-checks ticks */

    int64_t Ticks() const { return (int64_t)(dateData_ & TicksMask); }
    int32_t Kind() const { return (int32_t)(dateData_ >> 62); }

    static DateTime Now();      /* local wall clock, KindLocal (OS-backed, spec §9) */
    static DateTime UtcNow();   /* KindUtc */
    static DateTime Today();    /* Now() date component, KindLocal */
    static DateTime MinValue() { return DateTime(); }
    static DateTime MaxValue() { return FromTicksAndKind(MaxTicks, KindUnspecified); }
    static bool IsLeapYear(int32_t y) { if (y < 1 || y > 9999) throw std::runtime_error(""year out of range""); return (y % 4 == 0 && y % 100 != 0) || y % 400 == 0; }
    static int32_t DaysInMonth(int32_t y, int32_t m);
    static DateTime Parse(const std::string& s);   /* invariant O / s / G / yyyy-MM-dd (spec §9) */

    int32_t Year() const;  int32_t Month() const;  int32_t Day() const;
    int32_t Hour() const   { return (int32_t)((Ticks() / TimeSpan::TicksPerHour) % 24); }
    int32_t Minute() const { return (int32_t)((Ticks() / TimeSpan::TicksPerMinute) % 60); }
    int32_t Second() const { return (int32_t)((Ticks() / TimeSpan::TicksPerSecond) % 60); }
    int32_t Millisecond() const { return (int32_t)((Ticks() / TimeSpan::TicksPerMillisecond) % 1000); }
    int32_t DayOfWeek() const { return (int32_t)((Ticks() / TimeSpan::TicksPerDay + 1) % 7); } /* 0001-01-01 was Monday; Sunday=0 */
    int32_t DayOfYear() const;
    DateTime Date() const { return FromTicksAndKind(Ticks() - Ticks() % TimeSpan::TicksPerDay, Kind()); }

    /* checked addition in unsigned arithmetic so Ticks()+t can never overflow int64 (UB) */
    DateTime AddTicks(int64_t t) const {
        int64_t cur = Ticks();
        int64_t r = (int64_t)((uint64_t)cur + (uint64_t)t);
        if (((cur ^ r) & (t ^ r)) < 0) throw std::runtime_error(""DateTime ticks out of range"");
        return FromTicksAndKind(CheckedTicks(r), Kind());
    }
    DateTime AddMilliseconds(double v) const { return AddScaled(v, TimeSpan::TicksPerMillisecond); }
    DateTime AddSeconds(double v) const { return AddScaled(v, TimeSpan::TicksPerSecond); }
    DateTime AddMinutes(double v) const { return AddScaled(v, TimeSpan::TicksPerMinute); }
    DateTime AddHours(double v) const   { return AddScaled(v, TimeSpan::TicksPerHour); }
    DateTime AddDays(double v) const    { return AddScaled(v, TimeSpan::TicksPerDay); }
    DateTime AddMonths(int32_t m) const;   /* calendar op, day CLAMPED (Jan 31 + 1mo = Feb 28/29), spec §3 */
    DateTime AddYears(int32_t y) const { return AddMonths(y * 12); }
    DateTime Add(const TimeSpan& ts) const { return AddTicks(ts.Ticks()); }
    TimeSpan Subtract(const DateTime& o) const { return TimeSpan(Ticks() - o.Ticks()); }
    DateTime Subtract(const TimeSpan& ts) const { return AddTicks(-ts.Ticks()); }
    DateTime ToUniversalTime() const;   /* OS-backed; KindUtc treated as already-UTC; Unspecified assumed local (.NET rule) */
    DateTime ToLocalTime() const;       /* KindLocal already-local; Unspecified assumed UTC (.NET rule) */
    int32_t CompareTo(const DateTime& o) const { auto a = Ticks(), b = o.Ticks(); return a < b ? -1 : (a > b ? 1 : 0); }
    std::string ToString() const;                     /* invariant G: MM/dd/yyyy HH:mm:ss (spec §9) */
    std::string ToString(const std::string& fmt) const; /* token formatter: yyyy MM dd HH mm ss fff fffffff + literals; O/o/s shortcuts */

    /* ticks-only comparison; Kind is metadata (spec §3) */
    DateTime operator+(const TimeSpan& ts) const { return Add(ts); }
    DateTime operator-(const TimeSpan& ts) const { return Subtract(ts); }
    TimeSpan operator-(const DateTime& o) const { return Subtract(o); }
    bool operator==(const DateTime& o) const { return Ticks() == o.Ticks(); }
    bool operator!=(const DateTime& o) const { return Ticks() != o.Ticks(); }
    bool operator<(const DateTime& o) const  { return Ticks() < o.Ticks(); }
    bool operator<=(const DateTime& o) const { return Ticks() <= o.Ticks(); }
    bool operator>(const DateTime& o) const  { return Ticks() > o.Ticks(); }
    bool operator>=(const DateTime& o) const { return Ticks() >= o.Ticks(); }

private:
    void Init(int32_t y, int32_t mo, int32_t d, int32_t h, int32_t mi, int32_t s);  /* validates; throws on month 13 etc. */
    static int64_t CheckedTicks(int64_t t);        /* 0..MaxTicks or throw */
    DateTime AddScaled(double v, int64_t scale) const;  /* rounds to nearest ms like .NET Add(double) */
    /* civil-date math: days_from_civil / civil_from_days (Howard Hinnant algorithms, public domain);
       days since 0001-01-01 = days_from_civil(y,m,d) - days_from_civil(1,1,1) — see bcl_detail */
    void GetDateParts(int32_t& y, int32_t& mo, int32_t& d) const;
};

/* ================= TimeSpan bodies ================= */

inline TimeSpan TimeSpan::Interval(double v, int64_t scaleTicks) {
    if (v != v) throw std::runtime_error(""TimeSpan interval: value is NaN"");
    const double millisPerUnit = (double)(scaleTicks / TicksPerMillisecond);
    double millis = v * millisPerUnit + (v >= 0 ? 0.5 : -0.5);
    /* .NET bound: |millis| <= Int64.MaxValue / TicksPerMillisecond */
    if (!(millis > -922337203685477.0 && millis < 922337203685477.0))
        throw std::runtime_error(""TimeSpan overflow: interval out of range"");
    return TimeSpan((int64_t)millis * TicksPerMillisecond);
}

inline TimeSpan TimeSpan::CheckedAdd(int64_t a, int64_t b) {
    int64_t r = (int64_t)((uint64_t)a + (uint64_t)b);
    if (((a ^ r) & (b ^ r)) < 0) throw std::runtime_error(""TimeSpan overflow in addition"");
    return TimeSpan(r);
}

inline std::string TimeSpan::ToString() const {
    /* ""c"" invariant format: [-][d.]hh:mm:ss[.fffffff] */
    char buf[32];
    uint64_t mag = ticks_ < 0 ? 0ULL - (uint64_t)ticks_ : (uint64_t)ticks_;
    uint64_t days = mag / (uint64_t)TicksPerDay;
    uint64_t rem = mag % (uint64_t)TicksPerDay;
    uint32_t hh = (uint32_t)(rem / (uint64_t)TicksPerHour);
    uint32_t mm = (uint32_t)((rem / (uint64_t)TicksPerMinute) % 60u);
    uint32_t ss = (uint32_t)((rem / (uint64_t)TicksPerSecond) % 60u);
    uint64_t frac = rem % (uint64_t)TicksPerSecond;
    std::string out;
    if (ticks_ < 0) out += '-';
    if (days > 0) { std::snprintf(buf, sizeof buf, ""%llu."", (unsigned long long)days); out += buf; }
    std::snprintf(buf, sizeof buf, ""%02u:%02u:%02u"", hh, mm, ss);
    out += buf;
    if (frac != 0) { std::snprintf(buf, sizeof buf, "".%07llu"", (unsigned long long)frac); out += buf; }
    return out;
}

inline TimeSpan TimeSpan::Parse(const std::string& s) {
    using namespace bcl_detail;
    auto fail = [&s]() { throw std::runtime_error(""Invalid TimeSpan format: '"" + s + ""'""); };
    size_t pos = 0;
    bool neg = false;
    if (pos < s.size() && s[pos] == '-') { neg = true; ++pos; }
    int64_t first = 0, days = 0, hh = 0, mm = 0, ss = 0, frac = 0;
    if (!parse_digits(s, pos, 1, 8, first)) fail();
    if (pos < s.size() && s[pos] == '.') {          /* [d.]hh:mm:ss */
        ++pos;
        days = first;
        if (!parse_digits(s, pos, 2, 2, hh)) fail();
    } else {
        hh = first;
    }
    if (!expect_char(s, pos, ':') || !parse_digits(s, pos, 2, 2, mm)) fail();
    if (!expect_char(s, pos, ':') || !parse_digits(s, pos, 2, 2, ss)) fail();
    if (pos < s.size() && s[pos] == '.') {          /* [.fffffff], 1-7 digits, right-padded */
        ++pos;
        size_t start = pos;
        if (!parse_digits(s, pos, 1, 7, frac)) fail();
        for (size_t k = pos - start; k < 7; ++k) frac *= 10;
    }
    if (pos != s.size() || hh > 23 || mm > 59 || ss > 59) fail();
    int64_t t = days * TicksPerDay + hh * TicksPerHour + mm * TicksPerMinute + ss * TicksPerSecond + frac;
    return TimeSpan(neg ? -t : t);
}

/* ================= DateTime bodies ================= */

inline DateTime DateTime::FromTicksAndKind(int64_t ticks, int32_t kind) {
    if (ticks < 0 || ticks > MaxTicks) throw std::runtime_error(""DateTime ticks out of range"");
    if (kind < 0 || kind > 2) throw std::runtime_error(""DateTime kind out of range"");
    DateTime r;
    r.dateData_ = (uint64_t)ticks | ((uint64_t)(uint32_t)kind << 62);
    return r;
}

inline int32_t DateTime::DaysInMonth(int32_t y, int32_t m) {
    if (y < 1 || y > 9999) throw std::runtime_error(""year out of range"");
    if (m < 1 || m > 12) throw std::runtime_error(""month out of range"");
    static const int32_t days[12] = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
    return (m == 2 && IsLeapYear(y)) ? 29 : days[m - 1];
}

inline void DateTime::Init(int32_t y, int32_t mo, int32_t d, int32_t h, int32_t mi, int32_t s) {
    if (y < 1 || y > 9999) throw std::runtime_error(""DateTime year out of range"");
    if (mo < 1 || mo > 12) throw std::runtime_error(""DateTime month out of range"");
    if (d < 1 || d > DaysInMonth(y, mo)) throw std::runtime_error(""DateTime day out of range"");
    if (h < 0 || h > 23 || mi < 0 || mi > 59 || s < 0 || s > 59)
        throw std::runtime_error(""DateTime time component out of range"");
    int64_t days = bcl_detail::days_from_civil(y, mo, d) + bcl_detail::DaysTo1970;
    int64_t ticks = days * TimeSpan::TicksPerDay
                  + ((int64_t)h * 3600 + (int64_t)mi * 60 + s) * TimeSpan::TicksPerSecond;
    dateData_ = (uint64_t)ticks;   /* KindUnspecified */
}

inline void DateTime::GetDateParts(int32_t& y, int32_t& mo, int32_t& d) const {
    bcl_detail::civil_from_days(Ticks() / TimeSpan::TicksPerDay - bcl_detail::DaysTo1970, y, mo, d);
}

inline int32_t DateTime::Year() const  { int32_t y, mo, d; GetDateParts(y, mo, d); return y; }
inline int32_t DateTime::Month() const { int32_t y, mo, d; GetDateParts(y, mo, d); return mo; }
inline int32_t DateTime::Day() const   { int32_t y, mo, d; GetDateParts(y, mo, d); return d; }

inline int32_t DateTime::DayOfYear() const {
    int32_t y, mo, d;
    GetDateParts(y, mo, d);
    return (int32_t)(bcl_detail::days_from_civil(y, mo, d) - bcl_detail::days_from_civil(y, 1, 1)) + 1;
}

inline int64_t DateTime::CheckedTicks(int64_t t) {
    if (t < 0 || t > MaxTicks) throw std::runtime_error(""DateTime ticks out of range"");
    return t;
}

inline DateTime DateTime::AddScaled(double v, int64_t scale) const {
    if (v != v) throw std::runtime_error(""DateTime add: value is NaN"");
    const double millisPerUnit = (double)(scale / TimeSpan::TicksPerMillisecond);
    double millis = v * millisPerUnit + (v >= 0 ? 0.5 : -0.5);
    /* .NET bound: |millis| < MaxTicks / TicksPerMillisecond */
    if (!(millis > -315537897600000.0 && millis < 315537897600000.0))
        throw std::runtime_error(""DateTime ticks out of range"");
    return AddTicks((int64_t)millis * TimeSpan::TicksPerMillisecond);
}

inline DateTime DateTime::AddMonths(int32_t m) const {
    if (m < -120000 || m > 120000) throw std::runtime_error(""DateTime AddMonths: months out of range"");
    int32_t y, mo, d;
    GetDateParts(y, mo, d);
    int64_t months0 = (int64_t)(y - 1) * 12 + (mo - 1) + m;
    if (months0 < 0 || months0 >= 9999LL * 12) throw std::runtime_error(""DateTime ticks out of range"");
    int32_t ny = (int32_t)(months0 / 12) + 1;
    int32_t nmo = (int32_t)(months0 % 12) + 1;
    int32_t maxd = DaysInMonth(ny, nmo);
    int32_t nd = d < maxd ? d : maxd;   /* day CLAMPED to the target month's length */
    int64_t days = bcl_detail::days_from_civil(ny, nmo, nd) + bcl_detail::DaysTo1970;
    int64_t ticks = days * TimeSpan::TicksPerDay + Ticks() % TimeSpan::TicksPerDay;
    return FromTicksAndKind(CheckedTicks(ticks), Kind());
}

inline DateTime DateTime::UtcNow() {
    std::time_t t = std::time(nullptr);
    std::tm tmv {};
    if (t == (std::time_t)-1 || !bcl_detail::os_gm_time(t, tmv))
        throw std::runtime_error(""DateTime: OS UTC time out of range"");
    int sec = tmv.tm_sec > 59 ? 59 : tmv.tm_sec;   /* leap-second guard */
    DateTime r(tmv.tm_year + 1900, tmv.tm_mon + 1, tmv.tm_mday, tmv.tm_hour, tmv.tm_min, sec);
    return FromTicksAndKind(r.Ticks(), KindUtc);
}

inline DateTime DateTime::Now() {
    std::time_t t = std::time(nullptr);
    std::tm tmv {};
    if (t == (std::time_t)-1 || !bcl_detail::os_local_time(t, tmv))
        throw std::runtime_error(""DateTime: OS local time out of range"");
    int sec = tmv.tm_sec > 59 ? 59 : tmv.tm_sec;   /* leap-second guard */
    DateTime r(tmv.tm_year + 1900, tmv.tm_mon + 1, tmv.tm_mday, tmv.tm_hour, tmv.tm_min, sec);
    return FromTicksAndKind(r.Ticks(), KindLocal);
}

inline DateTime DateTime::Today() { return Now().Date(); }

inline DateTime DateTime::ToUniversalTime() const {
    if (Kind() == KindUtc) return *this;
    /* Local (or Unspecified assumed local, .NET rule) -> UTC via mktime */
    int32_t y, mo, d;
    GetDateParts(y, mo, d);
    if (y < 1970) throw std::runtime_error(""DateTime out of range for OS time conversion"");
    std::tm tmv {};
    tmv.tm_year = y - 1900; tmv.tm_mon = mo - 1; tmv.tm_mday = d;
    tmv.tm_hour = Hour(); tmv.tm_min = Minute(); tmv.tm_sec = Second();
    tmv.tm_isdst = -1;
    std::time_t t = std::mktime(&tmv);
    if (t == (std::time_t)-1) throw std::runtime_error(""DateTime out of range for OS time conversion"");
    int64_t sub = Ticks() % TimeSpan::TicksPerSecond;
    return FromTicksAndKind(
        CheckedTicks(bcl_detail::UnixEpochTicks + (int64_t)t * TimeSpan::TicksPerSecond + sub), KindUtc);
}

inline DateTime DateTime::ToLocalTime() const {
    if (Kind() == KindLocal) return *this;
    /* Utc (or Unspecified assumed UTC, .NET rule) -> local via localtime */
    int64_t unixSeconds = Ticks() / TimeSpan::TicksPerSecond
                        - bcl_detail::UnixEpochTicks / TimeSpan::TicksPerSecond;
    if (unixSeconds < 0)
        throw std::runtime_error(""DateTime out of range for OS local-time conversion"");
    std::tm tmv {};
    if (!bcl_detail::os_local_time((std::time_t)unixSeconds, tmv))
        throw std::runtime_error(""DateTime out of range for OS local-time conversion"");
    int sec = tmv.tm_sec > 59 ? 59 : tmv.tm_sec;   /* leap-second guard */
    DateTime r(tmv.tm_year + 1900, tmv.tm_mon + 1, tmv.tm_mday, tmv.tm_hour, tmv.tm_min, sec);
    int64_t sub = Ticks() % TimeSpan::TicksPerSecond;
    return FromTicksAndKind(r.Ticks() + sub, KindLocal);
}

inline std::string DateTime::ToString() const { return ToString(""MM/dd/yyyy HH:mm:ss""); }

inline std::string DateTime::ToString(const std::string& fmt) const {
    if (fmt == ""O"" || fmt == ""o"") return ToString(""yyyy-MM-ddTHH:mm:ss.fffffff"");
    if (fmt == ""s"") return ToString(""yyyy-MM-ddTHH:mm:ss"");
    int32_t y, mo, d;
    GetDateParts(y, mo, d);
    char buf[16];
    std::string out;
    size_t i = 0;
    auto starts = [&](const char* tok) {
        return fmt.compare(i, std::strlen(tok), tok) == 0;
    };
    while (i < fmt.size()) {
        if (starts(""yyyy""))         { std::snprintf(buf, sizeof buf, ""%04d"", y); out += buf; i += 4; }
        else if (starts(""fffffff"")) { std::snprintf(buf, sizeof buf, ""%07lld"", (long long)(Ticks() % TimeSpan::TicksPerSecond)); out += buf; i += 7; }
        else if (starts(""fff""))     { std::snprintf(buf, sizeof buf, ""%03d"", Millisecond()); out += buf; i += 3; }
        else if (starts(""MM""))      { std::snprintf(buf, sizeof buf, ""%02d"", mo); out += buf; i += 2; }
        else if (starts(""dd""))      { std::snprintf(buf, sizeof buf, ""%02d"", d); out += buf; i += 2; }
        else if (starts(""HH""))      { std::snprintf(buf, sizeof buf, ""%02d"", Hour()); out += buf; i += 2; }
        else if (starts(""mm""))      { std::snprintf(buf, sizeof buf, ""%02d"", Minute()); out += buf; i += 2; }
        else if (starts(""ss""))      { std::snprintf(buf, sizeof buf, ""%02d"", Second()); out += buf; i += 2; }
        else { out += fmt[i]; ++i; }   /* literal passthrough */
    }
    return out;
}

inline DateTime DateTime::Parse(const std::string& s) {
    using namespace bcl_detail;
    auto fail = [&s]() { throw std::runtime_error(""Invalid DateTime format: '"" + s + ""'""); };
    size_t pos = 0;
    int64_t y = 0, mo = 0, d = 0, h = 0, mi = 0, sec = 0, frac = 0;
    if (s.find('/') != std::string::npos) {
        /* invariant G: MM/dd/yyyy HH:mm:ss */
        if (!parse_digits(s, pos, 1, 2, mo) || !expect_char(s, pos, '/') ||
            !parse_digits(s, pos, 1, 2, d)  || !expect_char(s, pos, '/') ||
            !parse_digits(s, pos, 4, 4, y)  || !expect_char(s, pos, ' ') ||
            !parse_digits(s, pos, 1, 2, h)  || !expect_char(s, pos, ':') ||
            !parse_digits(s, pos, 2, 2, mi) || !expect_char(s, pos, ':') ||
            !parse_digits(s, pos, 2, 2, sec)) fail();
    } else {
        /* yyyy-MM-dd, optionally THH:mm:ss[.fffffff] (""s"" sortable / ""O"" round-trip) */
        if (!parse_digits(s, pos, 4, 4, y)  || !expect_char(s, pos, '-') ||
            !parse_digits(s, pos, 2, 2, mo) || !expect_char(s, pos, '-') ||
            !parse_digits(s, pos, 2, 2, d)) fail();
        if (pos < s.size()) {
            if (!expect_char(s, pos, 'T') ||
                !parse_digits(s, pos, 2, 2, h)  || !expect_char(s, pos, ':') ||
                !parse_digits(s, pos, 2, 2, mi) || !expect_char(s, pos, ':') ||
                !parse_digits(s, pos, 2, 2, sec)) fail();
            if (pos < s.size() && s[pos] == '.') {
                ++pos;
                size_t start = pos;
                if (!parse_digits(s, pos, 1, 7, frac)) fail();
                for (size_t k = pos - start; k < 7; ++k) frac *= 10;
            }
        }
    }
    if (pos != s.size()) fail();
    DateTime r((int32_t)y, (int32_t)mo, (int32_t)d, (int32_t)h, (int32_t)mi, (int32_t)sec);  /* validates ranges */
    return FromTicksAndKind(r.Ticks() + frac, KindUnspecified);
}

inline std::ostream& operator<<(std::ostream& os, const TimeSpan& v) { return os << v.ToString(); }
inline std::ostream& operator<<(std::ostream& os, const DateTime& v) { return os << v.ToString(); }

} /* namespace BasicLang */

template<> struct std::hash<BasicLang::TimeSpan> {
    size_t operator()(const BasicLang::TimeSpan& v) const noexcept { return std::hash<int64_t>{}(v.ticks_); }
};
template<> struct std::hash<BasicLang::DateTime> {   /* ticks only — Kind excluded, matches equality (spec §6.2) */
    size_t operator()(const BasicLang::DateTime& v) const noexcept { return std::hash<int64_t>{}(v.Ticks()); }
};
";
    }
}
