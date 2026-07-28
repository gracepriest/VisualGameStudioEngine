namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// Single source of truth for the native BCL value-type runtime header
    /// <c>bl_bcltypes.hpp</c> (spec: docs/superpowers/specs/2026-07-27-p1-native-bcl-types-design.md
    /// §3/§9): DateTime + TimeSpan (P1 Task 6) plus Guid, DateTimeOffset and StringBuilder
    /// (P1 Task 7) in one file.
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

/* ---- OS CSPRNG seam for Guid::NewGuid (spec §3: BCryptGenRandom on Windows;
   NEVER rand()/mt19937). Exact-signature extern declarations keep this header
   include-free AND conflict-free when a TU also includes <windows.h>/<bcrypt.h>.
   - MSVC (incl. clang targeting MSVC): direct BCryptGenRandom, linked via
     #pragma comment(lib, ""bcrypt"") — its signature uses only plain C++ types.
   - MinGW g++/clang++: #pragma comment(lib) is a no-op there and the test
     harness's fixed compile line cannot add -lbcrypt, so we call RtlGenRandom
     (SystemFunction036, advapi32 — in MinGW's DEFAULT link set). Same kernel
     CSPRNG as BCryptGenRandom, and its signature needs no Win32 struct tags —
     LoadLibraryA/GetProcAddress declarations would drag in HMODULE
     (struct HINSTANCE__*), which an include-free body opening its own namespace
     cannot forward-declare at global scope without closing the namespace.
   - POSIX: /dev/urandom (getrandom-backed on modern kernels). */
#if defined(_MSC_VER)
extern ""C"" long __stdcall BCryptGenRandom(void* hAlgorithm, unsigned char* pbBuffer,
                                          unsigned long cbBuffer, unsigned long dwFlags);
#pragma comment(lib, ""bcrypt"")
#elif defined(_WIN32)
extern ""C"" unsigned char __stdcall SystemFunction036(void* buffer, unsigned long length); /* RtlGenRandom */
#endif

inline void os_random_bytes(uint8_t* buf, uint32_t len) {
#if defined(_MSC_VER)
    if (BCryptGenRandom(nullptr, buf, len, 0x00000002UL /* BCRYPT_USE_SYSTEM_PREFERRED_RNG */) < 0)
        throw std::runtime_error(""Guid: OS CSPRNG failure (BCryptGenRandom)"");
#elif defined(_WIN32)
    if (!SystemFunction036(buf, len))
        throw std::runtime_error(""Guid: OS CSPRNG failure (RtlGenRandom)"");
#else
    std::FILE* f = std::fopen(""/dev/urandom"", ""rb"");
    if (!f) throw std::runtime_error(""Guid: OS CSPRNG unavailable (/dev/urandom)"");
    size_t got = std::fread(buf, 1, len, f);
    std::fclose(f);
    if (got != len) throw std::runtime_error(""Guid: OS CSPRNG short read (/dev/urandom)"");
#endif
}

/* DateTimeOffset offset validation (spec §3/§11): whole minutes, |offset| <= 14:00.
   Literal tick constants because TimeSpan is declared after bcl_detail. */
inline int16_t dto_validate_offset(int64_t offsetTicks) {
    if (offsetTicks % 600000000LL != 0)   /* TicksPerMinute */
        throw std::runtime_error(""DateTimeOffset offset must be specified in whole minutes"");
    if (offsetTicks < -504000000000LL || offsetTicks > 504000000000LL)   /* 14 hours */
        throw std::runtime_error(""DateTimeOffset offset out of range (+/-14:00)"");
    return (int16_t)(offsetTicks / 600000000LL);
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

/* ---- Guid: 16 bytes, .NET field layout. Spec §3. ---- */
struct Guid {
    int32_t a_ = 0; int16_t b_ = 0, c_ = 0; uint8_t d_[8] = {};
    Guid() = default;
    explicit Guid(const std::string& s) { *this = Parse(s); }  /* spec §5 ctor (String); New Guid(""..."") lowers here */
    static Guid NewGuid();                    /* v4 from OS CSPRNG: BCryptGenRandom (Windows) /
                                                 /dev/urandom (else). NEVER rand()/mt19937.
                                                 version nibble := 4, variant := 10xx (RFC 4122). */
    static Guid Empty() { return Guid(); }
    static Guid Parse(const std::string& s);  /* accepts D/N/B/P; throws on bad input */
    std::string ToString() const;             /* ""D"": lowercase 8-4-4-4-12 */
    std::string ToString(const std::string& fmt) const;   /* D N B P */
    /* NATIVE-ONLY (not on the BL surface, spec §5): tests + the §8 conversion pair use it */
    void ToByteArray(uint8_t out[16]) const;  /* .NET order: a,b,c little-endian then d_ verbatim (spec §8) */
    int32_t CompareTo(const Guid& o) const;   /* field-by-field a,b,c then bytes — NOT memcmp of ToByteArray */
    bool operator==(const Guid& o) const = default;
};

/* ---- DateTimeOffset: UTC DateTime + offset minutes. Spec §3. ---- */
struct DateTimeOffset {
    DateTime utc_;               /* stores the UTC instant, KindUnspecified */
    int16_t offsetMinutes_ = 0;  /* ±14h, whole minutes; ctor validates */
    DateTimeOffset() = default;
    explicit DateTimeOffset(const DateTime& dt);                 /* .NET Kind rules: Utc→offset 0; Local/Unspecified→local zone offset */
    DateTimeOffset(const DateTime& clockTime, const TimeSpan& offset);  /* validates offset; Kind rules per spec §3 sources (.NET: Utc+nonzero throws) */
    static DateTimeOffset Now();  static DateTimeOffset UtcNow();
    static DateTimeOffset FromUnixTimeSeconds(int64_t s);
    static DateTimeOffset FromUnixTimeMilliseconds(int64_t ms);
    DateTime UtcDateTime() const;             /* KindUtc, matching .NET (equality is ticks-only anyway) */
    DateTime LocalDateTime() const;
    DateTime ClockDateTime() const;           /* surfaced to BL as the 'DateTime' property */
    TimeSpan Offset() const { return TimeSpan((int64_t)offsetMinutes_ * TimeSpan::TicksPerMinute); }
    int64_t TicksValue() const { return ClockDateTime().Ticks(); }  /* BL 'Ticks' property = clock ticks (.NET) */
    DateTimeOffset ToOffset(const TimeSpan& o) const;
    DateTimeOffset ToUniversalTime() const { return DateTimeOffset(utc_, TimeSpan::Zero()); }
    DateTimeOffset ToLocalTime() const;
    int64_t ToUnixTimeSeconds() const;  int64_t ToUnixTimeMilliseconds() const;
    std::string ToString() const;             /* invariant: MM/dd/yyyy HH:mm:ss zzz (+HH:mm) */
    int32_t CompareTo(const DateTimeOffset& o) const { return utc_.CompareTo(o.utc_); }
    /* equality/ordering compare the UTC instant (spec §3) */
    bool operator==(const DateTimeOffset& o) const { return utc_ == o.utc_; }
    bool operator!=(const DateTimeOffset& o) const { return !(*this == o); }
    bool operator<(const DateTimeOffset& o) const  { return utc_ < o.utc_; }
    bool operator<=(const DateTimeOffset& o) const { return utc_ <= o.utc_; }
    bool operator>(const DateTimeOffset& o) const  { return utc_ > o.utc_; }
    bool operator>=(const DateTimeOffset& o) const { return utc_ >= o.utc_; }
};

/* ---- StringBuilder: the ONE reference type. Spec §3. UTF-8 byte semantics (spec §9). ---- */
class StringBuilder : public std::enable_shared_from_this<StringBuilder> {
    std::string buf_;
public:
    StringBuilder() = default;
    explicit StringBuilder(const std::string& s) : buf_(s) {}
    /* Append family returns shared_from_this() so chains emit uniformly with -> (spec §3).
       NB: requires the object to be OWNED by a shared_ptr — codegen always constructs via
       make_shared (Task 9), and the runtime tests must too. */
    std::shared_ptr<StringBuilder> Append(const std::string& s) { buf_ += s; return shared_from_this(); }
    /* REQUIRED: without it a literal Append(""..."") picks the BOOL overload — array-to-pointer
       + pointer->bool are STANDARD conversions and beat the user-defined one to std::string */
    std::shared_ptr<StringBuilder> Append(const char* s) { buf_ += s; return shared_from_this(); }
    std::shared_ptr<StringBuilder> Append(int32_t v) { buf_ += std::to_string(v); return shared_from_this(); }  /* REQUIRED: without it, Append(Integer) is ambiguous (int32->int64 and int32->double are both rank Conversion) */
    std::shared_ptr<StringBuilder> Append(int64_t v) { buf_ += std::to_string(v); return shared_from_this(); }
    std::shared_ptr<StringBuilder> Append(bool v) { buf_ += (v ? ""True"" : ""False""); return shared_from_this(); } /* else bool promotes to int and prints 1/0 vs .NET True/False */
    std::shared_ptr<StringBuilder> Append(double v);   /* invariant formatting, matches the backend's existing double->string style */
    std::shared_ptr<StringBuilder> AppendLine(const std::string& s = """") { buf_ += s; buf_ += ""\n""; return shared_from_this(); }
    std::shared_ptr<StringBuilder> AppendFormat(const std::string& fmt, const std::string& a0); /* {0} only, v1 */
    std::shared_ptr<StringBuilder> Insert(int32_t index, const std::string& s);   /* byte index; range-checked throw */
    std::shared_ptr<StringBuilder> Remove(int32_t start, int32_t len);            /* range-checked */
    std::shared_ptr<StringBuilder> Replace(const std::string& oldV, const std::string& newV);
    std::shared_ptr<StringBuilder> Clear() { buf_.clear(); return shared_from_this(); }
    std::string ToString() const { return buf_; }
    int32_t Length() const { return (int32_t)buf_.size(); }   /* UTF-8 BYTES, documented divergence */
    int32_t Capacity() const { return (int32_t)buf_.capacity(); }
};

/* ================= TimeSpan bodies ================= */

inline TimeSpan TimeSpan::Interval(double v, int64_t scaleTicks) {
    if (v != v) throw std::runtime_error(""TimeSpan interval: value is NaN"");
    const double millisPerUnit = (double)(scaleTicks / TicksPerMillisecond);
    double millis = v * millisPerUnit + (v >= 0 ? 0.5 : -0.5);
    /* .NET bound, INCLUSIVE: |millis| <= Int64.MaxValue / TicksPerMillisecond
       (.NET throws only on millis > Max || millis < Min, so the bound itself is legal) */
    if (!(millis >= -922337203685477.0 && millis <= 922337203685477.0))
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
    /* Day-magnitude guard: up to 8 day digits parse, so days * TicksPerDay could
       overflow int64 (UB) before any check — cap at the largest representable day
       count first. Components then accumulate SIGNED through CheckedAdd, which both
       turns in-range-days overflow into the §11 runtime_error and lets
       MinValue().ToString() round-trip (its magnitude is INT64_MAX + 1 — one tick
       more than a positive accumulation could ever hold). */
    if (days > 10675199) throw std::runtime_error(""TimeSpan overflow: '"" + s + ""' out of range"");
    const int64_t sign = neg ? -1 : 1;
    TimeSpan acc(0);
    acc = CheckedAdd(acc.ticks_, sign * (days * TicksPerDay));
    acc = CheckedAdd(acc.ticks_, sign * (hh * TicksPerHour));
    acc = CheckedAdd(acc.ticks_, sign * (mm * TicksPerMinute));
    acc = CheckedAdd(acc.ticks_, sign * (ss * TicksPerSecond));
    acc = CheckedAdd(acc.ticks_, sign * frac);
    return acc;
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

/* ================= Guid bodies ================= */

inline Guid Guid::NewGuid() {
    uint8_t b[16];
    bcl_detail::os_random_bytes(b, 16);
    Guid g;
    std::memcpy(&g.a_, b, 4);       /* random bytes — per-field endianness is irrelevant */
    std::memcpy(&g.b_, b + 4, 2);
    std::memcpy(&g.c_, b + 6, 2);
    std::memcpy(g.d_, b + 8, 8);
    g.c_ = (int16_t)((g.c_ & 0x0FFF) | 0x4000);    /* version nibble := 4 */
    g.d_[0] = (uint8_t)((g.d_[0] & 0x3F) | 0x80);  /* variant := 10xx (RFC 4122) */
    return g;
}

inline Guid Guid::Parse(const std::string& s) {
    auto fail = [&s]() { throw std::runtime_error(""Invalid Guid format: '"" + s + ""'""); };
    auto hexVal = [](char ch) -> int {
        if (ch >= '0' && ch <= '9') return ch - '0';
        if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
        if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
        return -1;
    };
    size_t off = 0, len = s.size();
    if (len == 38) {                               /* ""B""/""P"" wrappers */
        if (!((s[0] == '{' && s[37] == '}') || (s[0] == '(' && s[37] == ')'))) fail();
        off = 1; len = 36;
    }
    bool dashed = false;
    if (len == 36) dashed = true;                  /* ""D"" */
    else if (len != 32) fail();                    /* else ""N"" */
    uint8_t nib[32];
    size_t n = 0;
    for (size_t i = 0; i < len; ++i) {
        char ch = s[off + i];
        if (dashed && (i == 8 || i == 13 || i == 18 || i == 23)) {
            if (ch != '-') fail();
            continue;
        }
        int v = hexVal(ch);
        if (v < 0) fail();
        nib[n++] = (uint8_t)v;
    }
    /* n == 32 by the length/dash checks */
    uint32_t a = 0, b = 0, c = 0;
    for (int i = 0; i < 8; ++i)   a = (a << 4) | nib[i];
    for (int i = 8; i < 12; ++i)  b = (b << 4) | nib[i];
    for (int i = 12; i < 16; ++i) c = (c << 4) | nib[i];
    Guid g;
    g.a_ = (int32_t)a; g.b_ = (int16_t)b; g.c_ = (int16_t)c;
    for (int i = 0; i < 8; ++i) g.d_[i] = (uint8_t)((nib[16 + 2 * i] << 4) | nib[17 + 2 * i]);
    return g;
}

inline std::string Guid::ToString() const { return ToString(""D""); }

inline std::string Guid::ToString(const std::string& fmt) const {
    char d[40];
    std::snprintf(d, sizeof d, ""%08x-%04x-%04x-%02x%02x-%02x%02x%02x%02x%02x%02x"",
        (unsigned)(uint32_t)a_, (unsigned)(uint16_t)b_, (unsigned)(uint16_t)c_,
        (unsigned)d_[0], (unsigned)d_[1], (unsigned)d_[2], (unsigned)d_[3],
        (unsigned)d_[4], (unsigned)d_[5], (unsigned)d_[6], (unsigned)d_[7]);
    if (fmt.empty() || fmt == ""D"" || fmt == ""d"") return d;   /* .NET: empty == ""D"" */
    if (fmt == ""N"" || fmt == ""n"") {
        std::string out;
        for (const char* p = d; *p; ++p) if (*p != '-') out += *p;
        return out;
    }
    if (fmt == ""B"" || fmt == ""b"") return ""{"" + std::string(d) + ""}"";
    if (fmt == ""P"" || fmt == ""p"") return ""("" + std::string(d) + "")"";
    throw std::runtime_error(""Invalid Guid format specifier: '"" + fmt + ""'"");
}

inline void Guid::ToByteArray(uint8_t out[16]) const {
    const uint32_t a = (uint32_t)a_;
    const uint16_t b = (uint16_t)b_, c = (uint16_t)c_;
    out[0] = (uint8_t)a; out[1] = (uint8_t)(a >> 8); out[2] = (uint8_t)(a >> 16); out[3] = (uint8_t)(a >> 24);
    out[4] = (uint8_t)b; out[5] = (uint8_t)(b >> 8);
    out[6] = (uint8_t)c; out[7] = (uint8_t)(c >> 8);
    std::memcpy(out + 8, d_, 8);
}

inline int32_t Guid::CompareTo(const Guid& o) const {
    /* .NET order: _a,_b,_c compared as UNSIGNED field VALUES, then _d.._k bytes.
       NOT memcmp of ToByteArray (a/b/c are little-endian there) — verified vs real .NET. */
    const uint32_t a1 = (uint32_t)a_, a2 = (uint32_t)o.a_;
    if (a1 != a2) return a1 < a2 ? -1 : 1;
    const uint16_t b1 = (uint16_t)b_, b2 = (uint16_t)o.b_;
    if (b1 != b2) return b1 < b2 ? -1 : 1;
    const uint16_t c1 = (uint16_t)c_, c2 = (uint16_t)o.c_;
    if (c1 != c2) return c1 < c2 ? -1 : 1;
    for (int i = 0; i < 8; ++i)
        if (d_[i] != o.d_[i]) return d_[i] < o.d_[i] ? -1 : 1;
    return 0;
}

/* ================= DateTimeOffset bodies ================= */

inline DateTimeOffset::DateTimeOffset(const DateTime& dt) {
    if (dt.Kind() == DateTime::KindUtc) {
        offsetMinutes_ = 0;
        utc_ = DateTime::FromTicksAndKind(dt.Ticks(), DateTime::KindUnspecified);
    } else {
        /* Local, or Unspecified assumed local (.NET rule): offset = the local zone's offset
           at dt, via the OS seam (ToUniversalTime -> mktime; the >=1970 pin applies) */
        DateTime utc = dt.ToUniversalTime();
        offsetMinutes_ = bcl_detail::dto_validate_offset(dt.Ticks() - utc.Ticks());
        utc_ = DateTime::FromTicksAndKind(utc.Ticks(), DateTime::KindUnspecified);
    }
}

inline DateTimeOffset::DateTimeOffset(const DateTime& clockTime, const TimeSpan& offset) {
    offsetMinutes_ = bcl_detail::dto_validate_offset(offset.Ticks());
    /* .NET Kind rules: a Utc clock demands offset 0; a Local clock demands the zone's offset */
    if (clockTime.Kind() == DateTime::KindUtc && offsetMinutes_ != 0)
        throw std::runtime_error(""DateTimeOffset: offset must be zero for a Utc DateTime"");
    if (clockTime.Kind() == DateTime::KindLocal) {
        int64_t localOffset = clockTime.Ticks() - clockTime.ToUniversalTime().Ticks();
        if (localOffset != (int64_t)offsetMinutes_ * TimeSpan::TicksPerMinute)
            throw std::runtime_error(""DateTimeOffset: offset does not match the local time zone"");
    }
    utc_ = DateTime::FromTicksAndKind(
        clockTime.Ticks() - (int64_t)offsetMinutes_ * TimeSpan::TicksPerMinute,
        DateTime::KindUnspecified);   /* range-checks the UTC instant, like .NET's ValidateDate */
}

inline DateTimeOffset DateTimeOffset::Now() { return DateTimeOffset(DateTime::Now()); }
inline DateTimeOffset DateTimeOffset::UtcNow() { return DateTimeOffset(DateTime::UtcNow()); }

inline DateTimeOffset DateTimeOffset::FromUnixTimeSeconds(int64_t s) {
    /* .NET bounds: 0001-01-01..9999-12-31T23:59:59Z as Unix seconds, inclusive */
    if (s < -62135596800LL || s > 253402300799LL)
        throw std::runtime_error(""DateTimeOffset: Unix seconds out of range"");
    return DateTimeOffset(
        DateTime::FromTicksAndKind(bcl_detail::UnixEpochTicks + s * TimeSpan::TicksPerSecond,
                                   DateTime::KindUnspecified),
        TimeSpan::Zero());
}

inline DateTimeOffset DateTimeOffset::FromUnixTimeMilliseconds(int64_t ms) {
    if (ms < -62135596800000LL || ms > 253402300799999LL)
        throw std::runtime_error(""DateTimeOffset: Unix milliseconds out of range"");
    return DateTimeOffset(
        DateTime::FromTicksAndKind(bcl_detail::UnixEpochTicks + ms * TimeSpan::TicksPerMillisecond,
                                   DateTime::KindUnspecified),
        TimeSpan::Zero());
}

inline DateTime DateTimeOffset::UtcDateTime() const {
    return DateTime::FromTicksAndKind(utc_.Ticks(), DateTime::KindUtc);
}

inline DateTime DateTimeOffset::LocalDateTime() const {
    return UtcDateTime().ToLocalTime();   /* OS seam; KindLocal result */
}

inline DateTime DateTimeOffset::ClockDateTime() const {
    return DateTime::FromTicksAndKind(
        utc_.Ticks() + (int64_t)offsetMinutes_ * TimeSpan::TicksPerMinute,
        DateTime::KindUnspecified);
}

inline DateTimeOffset DateTimeOffset::ToOffset(const TimeSpan& o) const {
    DateTimeOffset r;
    r.utc_ = utc_;
    r.offsetMinutes_ = bcl_detail::dto_validate_offset(o.Ticks());
    (void)r.ClockDateTime();   /* range-check the shifted clock time (.NET throws here too) */
    return r;
}

inline DateTimeOffset DateTimeOffset::ToLocalTime() const {
    return DateTimeOffset(LocalDateTime());   /* KindLocal -> the local zone's offset */
}

inline int64_t DateTimeOffset::ToUnixTimeSeconds() const {
    /* .NET formula: divide the (nonnegative) UTC ticks, THEN shift by the epoch —
       this floors pre-1970 instants instead of truncating toward zero */
    return utc_.Ticks() / TimeSpan::TicksPerSecond - 62135596800LL;
}

inline int64_t DateTimeOffset::ToUnixTimeMilliseconds() const {
    return utc_.Ticks() / TimeSpan::TicksPerMillisecond - 62135596800000LL;
}

inline std::string DateTimeOffset::ToString() const {
    /* invariant ""MM/dd/yyyy HH:mm:ss zzz"" — verified vs real .NET */
    char buf[16];
    int32_t om = offsetMinutes_;
    const char sign = om < 0 ? '-' : '+';
    if (om < 0) om = -om;
    std::snprintf(buf, sizeof buf, "" %c%02d:%02d"", sign, om / 60, om % 60);
    return ClockDateTime().ToString() + buf;
}

/* ================= StringBuilder bodies ================= */

inline std::shared_ptr<StringBuilder> StringBuilder::Append(double v) {
    buf_ += std::to_string(v);   /* the backend's existing double->string style (CppCodeGenerator) */
    return shared_from_this();
}

inline std::shared_ptr<StringBuilder> StringBuilder::AppendFormat(const std::string& fmt, const std::string& a0) {
    size_t prev = 0, pos;
    while ((pos = fmt.find(""{0}"", prev)) != std::string::npos) {
        buf_.append(fmt, prev, pos - prev);
        buf_ += a0;
        prev = pos + 3;
    }
    buf_.append(fmt, prev, std::string::npos);
    return shared_from_this();
}

inline std::shared_ptr<StringBuilder> StringBuilder::Insert(int32_t index, const std::string& s) {
    if (index < 0 || (size_t)index > buf_.size())
        throw std::runtime_error(""StringBuilder: index out of range"");
    buf_.insert((size_t)index, s);
    return shared_from_this();
}

inline std::shared_ptr<StringBuilder> StringBuilder::Remove(int32_t start, int32_t len) {
    if (start < 0 || len < 0 || (size_t)start + (size_t)len > buf_.size())
        throw std::runtime_error(""StringBuilder: index out of range"");
    buf_.erase((size_t)start, (size_t)len);
    return shared_from_this();
}

inline std::shared_ptr<StringBuilder> StringBuilder::Replace(const std::string& oldV, const std::string& newV) {
    if (oldV.empty())
        throw std::runtime_error(""StringBuilder: Replace search string must not be empty"");
    size_t pos = 0;
    while ((pos = buf_.find(oldV, pos)) != std::string::npos) {
        buf_.replace(pos, oldV.size(), newV);
        pos += newV.size();
    }
    return shared_from_this();
}

inline std::ostream& operator<<(std::ostream& os, const TimeSpan& v) { return os << v.ToString(); }
inline std::ostream& operator<<(std::ostream& os, const DateTime& v) { return os << v.ToString(); }
/* spec §6.2 requires inserters for ALL FIVE value structs — Guid and DateTimeOffset too: */
inline std::ostream& operator<<(std::ostream& os, const Guid& v) { return os << v.ToString(); }
inline std::ostream& operator<<(std::ostream& os, const DateTimeOffset& v) { return os << v.ToString(); }
inline std::ostream& operator<<(std::ostream& os, const std::shared_ptr<StringBuilder>& sb) {
    return os << (sb ? sb->ToString() : std::string());
}

} /* namespace BasicLang */

template<> struct std::hash<BasicLang::TimeSpan> {
    size_t operator()(const BasicLang::TimeSpan& v) const noexcept { return std::hash<int64_t>{}(v.ticks_); }
};
template<> struct std::hash<BasicLang::DateTime> {   /* ticks only — Kind excluded, matches equality (spec §6.2) */
    size_t operator()(const BasicLang::DateTime& v) const noexcept { return std::hash<int64_t>{}(v.Ticks()); }
};
template<> struct std::hash<BasicLang::Guid> {
    size_t operator()(const BasicLang::Guid& v) const noexcept {
        uint8_t b[16]; v.ToByteArray(b);
        size_t h = 1469598103934665603ULL;                    /* FNV-1a over all 16 bytes */
        for (uint8_t x : b) { h ^= x; h *= 1099511628211ULL; }
        return h;
    }
};
template<> struct std::hash<BasicLang::DateTimeOffset> {      /* UTC instant — matches equality (spec §6.2) */
    size_t operator()(const BasicLang::DateTimeOffset& v) const noexcept {
        return std::hash<int64_t>{}(v.UtcDateTime().Ticks());
    }
};
";
    }
}
