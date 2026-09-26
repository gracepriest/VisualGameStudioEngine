namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// <c>BasicLang::Prim</c>: the C++ implementation of every row of <see cref="PrimitiveStaticSurface"/>
    /// — <c>String.Format</c>/<c>Join</c>/<c>Concat</c>/<c>IsNullOrEmpty</c>/<c>IsNullOrWhiteSpace</c>/
    /// <c>Empty</c>, <c>Boolean.Parse</c>, the <c>Char</c> predicates, and each numeric keyword's
    /// <c>Parse</c> / <c>MaxValue</c> / <c>MinValue</c> (plus Double/Single's <c>Epsilon</c>, <c>NaN</c>,
    /// infinities, <c>IsNaN</c>, <c>IsInfinity</c>). Each row is a function <c>Prim::Type_Member</c>, so a
    /// static property lowers to a call like a static method.
    ///
    /// <para><b>Composite formatting</b> (<c>String.Format</c>) follows .NET: <c>{index[,alignment][:format]}</c>,
    /// <c>{{</c>/<c>}}</c> escapes, <c>FormatException</c> for a malformed string or an index past the
    /// arguments. The format specifiers are the ones whose output is the SAME in every culture — <c>D</c>,
    /// <c>X</c>, <c>F</c>, <c>N</c> (group ',' decimal '.'), <c>E</c>, and <c>G</c> without a precision.
    /// ⛔ <c>F</c>/<c>N</c>/<c>E</c> round the EXACT binary value half to EVEN and keep the sign of a
    /// value that rounds to zero — MEASURED on .NET 8: 0.125:F2 is "0.12", 0.375:F2 "0.38", 2.5:F0 "2",
    /// -0.04:F1 "-0.0" (JavaScript's toFixed rounds 0.125 to "0.13", so neither backend may lean on a
    /// platform formatter). The exact expansion is taken with <c>to_chars</c> at a precision past any
    /// double's last significant digit and rounded here. <c>C</c>, <c>P</c> (culture-dependent),
    /// <c>G</c>-with-precision, <c>R</c> and custom patterns throw rather than print something .NET would not.</para>
    ///
    /// <para>Spliced UNCONDITIONALLY after <see cref="CppIntegerDivisionRuntime"/> in BOTH emission modes
    /// (GenerateHeader / EmitRuntimeHeader — keep in sync): it needs <c>NetException</c> and the BCL
    /// body's <c>FormatDouble</c>/<c>FormatSingle</c>. Include-free: &lt;string&gt;, &lt;limits&gt;,
    /// &lt;cmath&gt;, &lt;charconv&gt;, &lt;cstring&gt;, &lt;vector&gt;, &lt;memory&gt; are in both modes'
    /// unconditional include sets (&lt;cctype&gt; is avoided: the character tests are written out).
    /// Inline functions and templates only — ODR-safe.</para>
    /// </summary>
    public static class CppPrimitiveStaticsRuntime
    {
        public static string Source { get; } = Build();

        private static string Chain(string name)
        {
            CppExceptionTypes.TryGetInheritanceChain(name, out var chain);
            return chain;
        }

        private static string Build() => @"#ifndef BASICLANG_PRIM_RUNTIME
#define BASICLANG_PRIM_RUNTIME
namespace BasicLang {
namespace Prim {

[[noreturn]] inline void ThrowFormat(const std::string& message) {
    throw NetException(""" + Chain("FormatException") + @""", message);
}
[[noreturn]] inline void ThrowOverflow(const std::string& message) {
    throw NetException(""" + Chain("OverflowException") + @""", message);
}

inline bool IsWhite(char c) { return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\v' || c == '\f'; }

inline std::string Trim(const std::string& s) {
    size_t b = 0, e = s.size();
    while (b < e && IsWhite(s[b])) ++b;
    while (e > b && IsWhite(s[e - 1])) --e;
    return s.substr(b, e - b);
}

/* One formattable argument: the value with its .NET kind and, for integers, its width. */
struct Arg {
    enum Kind { Signed, Unsigned, Dbl, Sgl, Bool, Chr, Str, Obj } kind;
    int64_t i = 0; uint64_t u = 0; double d = 0; bool b = false; char c = 0; std::string s; int bits = 32;
    /* EVERY integral type but bool and char, by template: a Long literal renders as `3000000000LL`,
       a `long long`, which is int64_t on MSVC but a distinct type beside int64_t (`long`) on Linux,
       so a fixed-width overload set is ambiguous on one platform or duplicate on the other. */
    template <typename T, typename std::enable_if<std::is_integral<T>::value && !std::is_same<T, bool>::value
                                                  && !std::is_same<T, char>::value, int>::type = 0>
    Arg(T v) : kind(std::is_signed<T>::value ? Signed : Unsigned), bits((int)sizeof(T) * 8) {
        if (std::is_signed<T>::value) i = (int64_t)v; else u = (uint64_t)v;
    }
    Arg(double v) : kind(Dbl), d(v) {}
    Arg(float v) : kind(Sgl), d(v) {}
    Arg(bool v) : kind(Bool), b(v) {}
    Arg(char v) : kind(Chr), c(v) {}
    Arg(const std::string& v) : kind(Str), s(v) {}
    Arg(const char* v) : kind(Str), s(v ? v : """") {}
    /* Decimal, DateTime, TimeSpan … carry their own .NET-faithful ToString(). */
    template <typename T, typename = decltype(std::declval<const T&>().ToString())>
    Arg(const T& v) : kind(Obj), s(v.ToString()) {}

    bool IsIntegral() const { return kind == Signed || kind == Unsigned; }
    bool IsFloating() const { return kind == Dbl || kind == Sgl; }

    std::string Text() const {
        switch (kind) {
            case Signed: return std::to_string(i);
            case Unsigned: return std::to_string(u);
            case Dbl: return FormatDouble(d);
            case Sgl: return FormatSingle((float)d);
            case Bool: return b ? ""True"" : ""False"";
            case Chr: return std::string(1, c);
            default: return s;
        }
    }
};

/* The exact decimal expansion of |v| as (integral digits, fraction digits), untruncated. */
inline void ExactDigits(double v, std::string& intPart, std::string& fracPart) {
    char buf[1600];
    auto r = std::to_chars(buf, buf + sizeof(buf), std::fabs(v), std::chars_format::fixed, 1100);
    std::string all(buf, r.ptr);
    auto dot = all.find('.');
    intPart = all.substr(0, dot);
    fracPart = dot == std::string::npos ? std::string() : all.substr(dot + 1);
}

/* Round an EXACT digit string (no sign, no point) to `keep` digits, half to EVEN — what .NET 8
   measurably does: 0.125:F2 is 0.12, 0.375:F2 is 0.38, 2.5:F0 is 2. Returns the carry. */
inline bool RoundDigits(std::string& digits, size_t keep) {
    if (keep >= digits.size()) { digits.append(keep - digits.size(), '0'); return false; }
    bool up;
    if (digits[keep] != '5') up = digits[keep] > '5';
    else {
        bool beyond = digits.find_first_not_of('0', keep + 1) != std::string::npos;
        bool odd = keep > 0 && ((digits[keep - 1] - '0') & 1);
        up = beyond || odd;
    }
    digits.resize(keep);
    if (!up) return false;
    for (size_t k = keep; k-- > 0;) {
        if (digits[k] == '9') { digits[k] = '0'; continue; }
        ++digits[k];
        return false;
    }
    return true;
}

inline std::string GroupThousands(const std::string& intDigits) {
    std::string out;
    int n = (int)intDigits.size();
    for (int k = 0; k < n; ++k) {
        out += intDigits[k];
        int left = n - k - 1;
        if (left > 0 && left % 3 == 0) out += ',';
    }
    return out;
}

/* Fixed-point text of v with `decimals` digits, half to even; grouped for N. */
inline std::string FixedText(double v, int decimals, bool group) {
    if (std::isnan(v)) return ""NaN"";
    if (std::isinf(v)) return v > 0 ? ""Infinity"" : ""-Infinity"";
    std::string ip, fp;
    ExactDigits(v, ip, fp);
    std::string digits = ip + fp;
    bool carry = RoundDigits(digits, ip.size() + (size_t)decimals);
    if (carry) { digits.insert(digits.begin(), '1'); ip.insert(ip.begin(), '0'); }
    std::string intDigits = digits.substr(0, digits.size() - (size_t)decimals);
    std::string frac = digits.substr(digits.size() - (size_t)decimals);
    size_t nz = intDigits.find_first_not_of('0');
    intDigits = nz == std::string::npos ? ""0"" : intDigits.substr(nz);
    /* .NET 8 keeps the sign of a value that rounds to zero, and of -0.0: -0.04:F1 is ""-0.0"". */
    std::string out = std::signbit(v) ? ""-"" : """";
    out += group ? GroupThousands(intDigits) : intDigits;
    if (decimals > 0) out += ""."" + frac;
    return out;
}

/* d.ddddE+ddd (.NET's E: at least three exponent digits), half to even. */
inline std::string ExpText(double v, int decimals, char e) {
    if (std::isnan(v)) return ""NaN"";
    if (std::isinf(v)) return v > 0 ? ""Infinity"" : ""-Infinity"";
    std::string ip, fp;
    ExactDigits(v, ip, fp);
    std::string digits = ip + fp;
    size_t first = digits.find_first_not_of('0');
    int exp = 0;
    std::string mant;
    if (first == std::string::npos) { mant = std::string((size_t)decimals + 1, '0'); }
    else {
        exp = (int)ip.size() - (int)first - 1;
        mant = digits.substr(first);
        if (RoundDigits(mant, (size_t)decimals + 1)) { mant.insert(mant.begin(), '1'); mant.resize((size_t)decimals + 1); ++exp; }
    }
    std::string out = std::signbit(v) ? ""-"" : """";
    out += mant[0];
    if (decimals > 0) out += ""."" + mant.substr(1);
    std::string ex = std::to_string(exp < 0 ? -exp : exp);
    while (ex.size() < 3) ex.insert(ex.begin(), '0');
    out += e; out += exp < 0 ? '-' : '+'; out += ex;
    return out;
}

[[noreturn]] inline void Unsupported(const std::string& spec) {
    throw std::runtime_error(""String.Format: the format specifier '"" + spec +
        ""' is not supported on the C++ backend (D, X, F, N, E and G are; C and P depend on the culture)"");
}

inline std::string ApplyFormat(const Arg& a, const std::string& spec) {
    if (spec.empty()) return a.Text();
    if (a.kind == Arg::Str || a.kind == Arg::Chr || a.kind == Arg::Bool) return a.Text();  /* not IFormattable: .NET ignores it */
    if (a.kind == Arg::Obj) Unsupported(spec);  /* Decimal/DateTime ARE IFormattable — never drop their format silently */
    char letter = spec[0];
    bool hasPrec = spec.size() > 1;
    int prec = 0;
    for (size_t k = 1; k < spec.size(); ++k) {
        if (spec[k] < '0' || spec[k] > '9' || k > 3) Unsupported(spec);
        prec = prec * 10 + (spec[k] - '0');
    }
    switch (letter) {
        case 'D': case 'd': {
            if (!a.IsIntegral()) ThrowFormat(""Format specifier was invalid."");
            bool neg = a.kind == Arg::Signed && a.i < 0;
            std::string digits = a.kind == Arg::Signed
                ? std::to_string(neg ? (uint64_t)0 - (uint64_t)a.i : (uint64_t)a.i) : std::to_string(a.u);
            while ((int)digits.size() < prec) digits.insert(digits.begin(), '0');
            return (neg ? ""-"" : """") + digits;
        }
        case 'X': case 'x': {
            if (!a.IsIntegral()) ThrowFormat(""Format specifier was invalid."");
            uint64_t bits = a.kind == Arg::Signed ? (uint64_t)a.i : a.u;
            if (a.bits < 64) bits &= (((uint64_t)1 << a.bits) - 1);  /* two's complement at the type's width */
            const char* hex = letter == 'X' ? ""0123456789ABCDEF"" : ""0123456789abcdef"";
            std::string digits;
            do { digits.insert(digits.begin(), hex[bits & 15]); bits >>= 4; } while (bits);
            while ((int)digits.size() < prec) digits.insert(digits.begin(), '0');
            return digits;
        }
        case 'F': case 'f': case 'N': case 'n': {
            double v = a.IsIntegral() ? (a.kind == Arg::Signed ? (double)a.i : (double)a.u) : a.d;
            int decimals = hasPrec ? prec : 2;
            bool group = letter == 'N' || letter == 'n';
            /* An integer's digits are exact at any width: format it directly, never through double. */
            if (a.IsIntegral()) {
                bool neg = a.kind == Arg::Signed && a.i < 0;
                std::string intDigits = a.kind == Arg::Signed
                    ? std::to_string(neg ? (uint64_t)0 - (uint64_t)a.i : (uint64_t)a.i) : std::to_string(a.u);
                std::string out = neg ? ""-"" : """";
                out += group ? GroupThousands(intDigits) : intDigits;
                if (decimals > 0) out += ""."" + std::string((size_t)decimals, '0');
                return out;
            }
            if (a.kind == Arg::Sgl) v = (double)(float)v;
            return FixedText(v, decimals, group);
        }
        case 'E': case 'e': {
            double v = a.IsIntegral() ? (a.kind == Arg::Signed ? (double)a.i : (double)a.u) : a.d;
            return ExpText(v, hasPrec ? prec : 6, letter);
        }
        case 'G': case 'g':
            if (hasPrec) Unsupported(spec);
            return a.Text();
        default:
            Unsupported(spec);
    }
}

inline std::string Pad(const std::string& text, int alignment) {
    int width = alignment < 0 ? -alignment : alignment;
    if ((int)text.size() >= width) return text;
    std::string pad((size_t)width - text.size(), ' ');
    return alignment < 0 ? text + pad : pad + text;
}

inline std::string FormatCore(const std::string& fmt, const std::vector<Arg>& args) {
    std::string out;
    size_t k = 0, n = fmt.size();
    auto bad = [] { ThrowFormat(""Input string was not in a correct format.""); };
    while (k < n) {
        char c = fmt[k];
        if (c == '}') {
            if (k + 1 < n && fmt[k + 1] == '}') { out += '}'; k += 2; continue; }
            bad();
        }
        if (c != '{') { out += c; ++k; continue; }
        if (k + 1 < n && fmt[k + 1] == '{') { out += '{'; k += 2; continue; }
        ++k;
        size_t indexStart = k;
        while (k < n && fmt[k] >= '0' && fmt[k] <= '9') ++k;
        if (k == indexStart) bad();
        size_t index = std::stoul(fmt.substr(indexStart, k - indexStart));
        while (k < n && fmt[k] == ' ') ++k;
        int alignment = 0;
        if (k < n && fmt[k] == ',') {
            ++k;
            while (k < n && fmt[k] == ' ') ++k;
            bool neg = k < n && fmt[k] == '-';
            if (neg) ++k;
            size_t alignStart = k;
            while (k < n && fmt[k] >= '0' && fmt[k] <= '9') ++k;
            if (k == alignStart) bad();
            alignment = std::stoi(fmt.substr(alignStart, k - alignStart));
            if (neg) alignment = -alignment;
            while (k < n && fmt[k] == ' ') ++k;
        }
        std::string spec;
        if (k < n && fmt[k] == ':') {
            ++k;
            while (k < n && fmt[k] != '}') {
                if (fmt[k] == '{') bad();
                spec += fmt[k++];
            }
        }
        if (k >= n || fmt[k] != '}') bad();
        ++k;
        if (index >= args.size())
            ThrowFormat(""Index (zero based) must be greater than or equal to zero and less than the size of the argument list."");
        out += Pad(ApplyFormat(args[index], spec), alignment);
    }
    return out;
}

template <typename... A>
inline std::string String_Format(const std::string& fmt, const A&... args) {
    return FormatCore(fmt, std::vector<Arg>{ Arg(args)... });
}

template <typename T>
inline std::string JoinRange(const std::string& sep, const std::vector<T>& items) {
    std::string out;
    for (size_t k = 0; k < items.size(); ++k) { if (k) out += sep; out += Arg(items[k]).Text(); }
    return out;
}

/* Join(sep, array) / Join(sep, List) join the ELEMENTS, as .NET's IEnumerable overloads do. */
template <typename T>
inline std::string String_Join(const std::string& sep, const std::vector<T>& items) { return JoinRange(sep, items); }
/* Any shared_ptr collection with Count() and operator[] (BasicLang::List) — not named, because the
   collections runtime is only spliced into a program that uses one. */
template <typename C>
inline std::string String_Join(const std::string& sep, const std::shared_ptr<C>& items) {
    std::string out;
    int32_t n = items ? items->Count() : 0;
    for (int32_t k = 0; k < n; ++k) { if (k) out += sep; out += Arg((*items)[k]).Text(); }
    return out;
}
template <typename... A>
inline std::string String_Join(const std::string& sep, const A&... values) {
    return JoinRange(sep, std::vector<std::string>{ Arg(values).Text()... });
}

template <typename... A>
inline std::string String_Concat(const A&... values) {
    std::string out;
    ((out += Arg(values).Text()), ...);
    return out;
}

inline bool String_IsNullOrEmpty(const std::string& s) { return s.empty(); }
inline bool String_IsNullOrWhiteSpace(const std::string& s) {
    for (char c : s) if (!IsWhite(c)) return false;
    return true;
}
inline std::string String_Empty() { return std::string(); }

inline bool Boolean_Parse(const std::string& s) {
    std::string t = Trim(s);
    auto eq = [&](const char* w) {
        size_t len = std::strlen(w);
        if (t.size() != len) return false;
        for (size_t k = 0; k < len; ++k) {
            char a = t[k], b = w[k];
            if (a >= 'A' && a <= 'Z') a = (char)(a - 'A' + 'a');
            if (a != b) return false;
        }
        return true;
    };
    if (eq(""true"")) return true;
    if (eq(""false"")) return false;
    ThrowFormat(""String '"" + s + ""' was not recognized as a valid Boolean."");
}

inline bool Char_IsDigit(char c) { return c >= '0' && c <= '9'; }
inline bool Char_IsUpper(char c) { return c >= 'A' && c <= 'Z'; }
inline bool Char_IsLower(char c) { return c >= 'a' && c <= 'z'; }
inline bool Char_IsLetter(char c) { return Char_IsUpper(c) || Char_IsLower(c); }
inline bool Char_IsLetterOrDigit(char c) { return Char_IsLetter(c) || Char_IsDigit(c); }
inline bool Char_IsWhiteSpace(char c) { return IsWhite(c); }
inline char Char_ToUpper(char c) { return Char_IsLower(c) ? (char)(c - 'a' + 'A') : c; }
inline char Char_ToLower(char c) { return Char_IsUpper(c) ? (char)(c - 'A' + 'a') : c; }

/* .NET NumberStyles.Integer: surrounding white space, one leading sign, decimal digits. */
template <typename T>
inline T ParseIntegral(const std::string& s, const char* netName) {
    std::string t = Trim(s);
    size_t k = 0;
    bool neg = false;
    if (k < t.size() && (t[k] == '+' || t[k] == '-')) { neg = t[k] == '-'; ++k; }
    if (k >= t.size()) ThrowFormat(""The input string '"" + s + ""' was not in a correct format."");
    uint64_t mag = 0;
    bool overflow = false;
    for (; k < t.size(); ++k) {
        char c = t[k];
        if (c < '0' || c > '9') ThrowFormat(""The input string '"" + s + ""' was not in a correct format."");
        uint64_t digit = (uint64_t)(c - '0');
        if (mag > (std::numeric_limits<uint64_t>::max() - digit) / 10) overflow = true;
        else mag = mag * 10 + digit;
    }
    const std::string range = std::string(""Value was either too large or too small for "") + netName + ""."";
    if (overflow) ThrowOverflow(range);
    if constexpr (std::numeric_limits<T>::is_signed) {
        uint64_t limit = neg ? (uint64_t)std::numeric_limits<T>::max() + 1 : (uint64_t)std::numeric_limits<T>::max();
        if (mag > limit) ThrowOverflow(range);
        return neg ? (T)(0 - (int64_t)(mag - 1) - 1) : (T)mag;
    } else {
        if (neg && mag != 0) ThrowOverflow(range);
        if (mag > (uint64_t)std::numeric_limits<T>::max()) ThrowOverflow(range);
        return (T)mag;
    }
}

/* .NET NumberStyles.Float | AllowThousands (invariant): white space, sign, digits with ',' groups,
   '.', exponent; NaN / Infinity / -Infinity / the infinity sign, case-insensitively; out of range → ±Infinity. */
inline double ParseFloating(const std::string& s) {
    std::string t = Trim(s);
    std::string lower;
    for (char c : t) lower += (c >= 'A' && c <= 'Z') ? (char)(c - 'A' + 'a') : c;
    if (lower == ""nan"" || lower == ""-nan"" || lower == ""+nan"") return std::numeric_limits<double>::quiet_NaN();
    if (lower == ""infinity"" || lower == ""+infinity"" || lower == ""∞"" || lower == ""+∞"") return std::numeric_limits<double>::infinity();
    if (lower == ""-infinity"" || lower == ""-∞"") return -std::numeric_limits<double>::infinity();
    std::string clean;
    size_t k = 0;
    bool neg = false;
    if (k < t.size() && (t[k] == '+' || t[k] == '-')) { neg = t[k] == '-'; ++k; }
    bool digits = false;
    while (k < t.size() && ((t[k] >= '0' && t[k] <= '9') || (t[k] == ',' && digits))) {
        if (t[k] != ',') { clean += t[k]; digits = true; }
        ++k;
    }
    if (k < t.size() && t[k] == '.') {
        clean += t[k++];
        while (k < t.size() && t[k] >= '0' && t[k] <= '9') { clean += t[k++]; digits = true; }
    }
    if (!digits) ThrowFormat(""The input string '"" + s + ""' was not in a correct format."");
    if (k < t.size() && (t[k] == 'e' || t[k] == 'E')) {
        std::string ex = ""e"";
        ++k;
        if (k < t.size() && (t[k] == '+' || t[k] == '-')) ex += t[k++];
        size_t expStart = k;
        while (k < t.size() && t[k] >= '0' && t[k] <= '9') ex += t[k++];
        if (k == expStart) ThrowFormat(""The input string '"" + s + ""' was not in a correct format."");
        clean += ex;
    }
    if (k != t.size()) ThrowFormat(""The input string '"" + s + ""' was not in a correct format."");
    if (clean[0] == '.') clean.insert(clean.begin(), '0');
    double v = 0;
    auto r = std::from_chars(clean.data(), clean.data() + clean.size(), v);
    if (r.ec == std::errc::result_out_of_range) {
        /* Too large → Infinity (.NET Core 3.0+); too small → 0. Tell them apart by the exponent. */
        auto e = clean.find('e');
        bool big = e == std::string::npos || clean[e + 1] != '-';
        v = big ? std::numeric_limits<double>::infinity() : 0.0;
    }
    return neg ? -v : v;
}

#define BL_PRIM_INTEGRAL(BL, CT, NET) \
    inline CT BL##_Parse(const std::string& s) { return ParseIntegral<CT>(s, NET); } \
    inline CT BL##_MaxValue() { return std::numeric_limits<CT>::max(); } \
    inline CT BL##_MinValue() { return std::numeric_limits<CT>::min(); }
BL_PRIM_INTEGRAL(Integer, int32_t, ""an Int32"")
BL_PRIM_INTEGRAL(Long, int64_t, ""an Int64"")
BL_PRIM_INTEGRAL(Short, int16_t, ""an Int16"")
BL_PRIM_INTEGRAL(Byte, uint8_t, ""an unsigned byte"")
BL_PRIM_INTEGRAL(UShort, uint16_t, ""a UInt16"")
BL_PRIM_INTEGRAL(UInteger, uint32_t, ""a UInt32"")
BL_PRIM_INTEGRAL(ULong, uint64_t, ""a UInt64"")
#undef BL_PRIM_INTEGRAL

#define BL_PRIM_FLOATING(BL, CT) \
    inline CT BL##_Parse(const std::string& s) { return (CT)ParseFloating(s); } \
    inline CT BL##_MaxValue() { return std::numeric_limits<CT>::max(); } \
    inline CT BL##_MinValue() { return std::numeric_limits<CT>::lowest(); } \
    inline CT BL##_Epsilon() { return std::numeric_limits<CT>::denorm_min(); } \
    inline CT BL##_NaN() { return std::numeric_limits<CT>::quiet_NaN(); } \
    inline CT BL##_PositiveInfinity() { return std::numeric_limits<CT>::infinity(); } \
    inline CT BL##_NegativeInfinity() { return -std::numeric_limits<CT>::infinity(); } \
    inline bool BL##_IsNaN(CT v) { return std::isnan(v); } \
    inline bool BL##_IsInfinity(CT v) { return std::isinf(v); }
BL_PRIM_FLOATING(Double, double)
BL_PRIM_FLOATING(Single, float)
#undef BL_PRIM_FLOATING

} /* namespace Prim */
} /* namespace BasicLang */
#endif /* BASICLANG_PRIM_RUNTIME */
";
    }
}
