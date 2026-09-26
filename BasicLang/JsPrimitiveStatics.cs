using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen.JavaScript
{
    /// <summary>
    /// The JavaScript lowering of <see cref="PrimitiveStaticSurface"/> — <c>String.Format</c>,
    /// <c>Integer.Parse</c>, <c>Double.NaN</c> and the rest. Stateless: <see cref="JavaScriptCodeGenerator"/>
    /// asks <see cref="TryLowerCall"/> / <see cref="TryLowerProperty"/> and emits <see cref="Prelude"/> when
    /// <see cref="IsUsed"/> finds a row anywhere in the module.
    ///
    /// <para><b>Measured before</b> (master f8ad07c): <c>String.Format</c> was "no lowering", and
    /// <c>String.Empty &amp; "x"</c> printed "undefinedx" — <c>String.Empty</c> read a property of JS's
    /// own <c>String</c> constructor, which has none.</para>
    ///
    /// <para><b>Types.</b> Char and Long never reach here — the capability checker refuses both
    /// types on this backend (BL7004 / BL7003) — so the Char rows and Long's are unreachable, and
    /// a keyword member outside the table is refused like any other missing lowering.</para>
    ///
    /// <para><b>Composite formatting</b> mirrors <c>CppPrimitiveStaticsRuntime</c> exactly: the same
    /// specifier set (D X F N E, G without a precision), the same half-to-EVEN rounding of the exact
    /// binary value — ⛔ never <c>toFixed</c>, which rounds 0.125 to "0.13" where .NET 8 says "0.12" —
    /// and the same exceptions. The exact decimal expansion comes from the double's bits via BigInt.
    /// A Double argument formats as .NET's shortest round-trip text (the C++ FormatDouble rule: fixed
    /// unless the decimal exponent is ≥ 17, or ≥ 9 for Single, or &lt; -4).</para>
    /// </summary>
    internal static class JsPrimitiveStatics
    {
        /// <summary>Every helper the prelude defines — one bundle, emitted when any row is used.</summary>
        public const string Prelude = @"function __blExact(v) {
    const view = new DataView(new ArrayBuffer(8));
    view.setFloat64(0, Math.abs(v));
    const hi = view.getUint32(0), lo = view.getUint32(4);
    const bexp = (hi >>> 20) & 0x7ff;
    let mant = (BigInt(hi & 0xfffff) << 32n) | BigInt(lo);
    let e;
    if (bexp === 0) e = -1074; else { mant |= 1n << 52n; e = bexp - 1075; }
    if (e >= 0) return [(mant << BigInt(e)).toString(), """"];
    const k = -e;
    const scaled = (mant * 5n ** BigInt(k)).toString().padStart(k + 1, ""0"");
    return [scaled.slice(0, scaled.length - k), scaled.slice(scaled.length - k)];
}
function __blRound(digits, keep) {
    if (keep >= digits.length) return [digits.padEnd(keep, ""0""), false];
    let up;
    if (digits[keep] !== ""5"") up = digits[keep] > ""5"";
    else up = /[1-9]/.test(digits.slice(keep + 1)) || (keep > 0 && (digits.charCodeAt(keep - 1) - 48) % 2 === 1);
    const d = digits.slice(0, keep).split("""");
    if (!up) return [d.join(""""), false];
    for (let k = keep - 1; k >= 0; k--) {
        if (d[k] === ""9"") { d[k] = ""0""; continue; }
        d[k] = String.fromCharCode(d[k].charCodeAt(0) + 1);
        return [d.join(""""), false];
    }
    return [d.join(""""), true];
}
function __blGroup(s) {
    let out = """";
    for (let k = 0; k < s.length; k++) {
        out += s[k];
        const left = s.length - k - 1;
        if (left > 0 && left % 3 === 0) out += "","";
    }
    return out;
}
function __blSpecial(v) {
    if (Number.isNaN(v)) return ""NaN"";
    if (v === Infinity) return ""Infinity"";
    if (v === -Infinity) return ""-Infinity"";
    return null;
}
function __blFixed(v, decimals, group) {
    const sp = __blSpecial(v); if (sp !== null) return sp;
    const [ip, fp] = __blExact(v);
    let [digits, carry] = __blRound(ip + fp, ip.length + decimals);
    if (carry) digits = ""1"" + digits;
    let intDigits = digits.slice(0, digits.length - decimals).replace(/^0+/, """") || ""0"";
    const frac = digits.slice(digits.length - decimals);
    const neg = v < 0 || Object.is(v, -0);
    return (neg ? ""-"" : """") + (group ? __blGroup(intDigits) : intDigits) + (decimals > 0 ? ""."" + frac : """");
}
function __blExp(v, decimals, letter) {
    const sp = __blSpecial(v); if (sp !== null) return sp;
    const [ip, fp] = __blExact(v);
    const digits = ip + fp;
    const first = digits.search(/[1-9]/);
    let exp = 0, mant;
    if (first < 0) mant = ""0"".repeat(decimals + 1);
    else {
        exp = ip.length - first - 1;
        let carry;
        [mant, carry] = __blRound(digits.slice(first), decimals + 1);
        if (carry) { mant = (""1"" + mant).slice(0, decimals + 1); exp++; }
    }
    const neg = v < 0 || Object.is(v, -0);
    const ex = String(Math.abs(exp)).padStart(3, ""0"");
    return (neg ? ""-"" : """") + mant[0] + (decimals > 0 ? ""."" + mant.slice(1) : """") + letter + (exp < 0 ? ""-"" : ""+"") + ex;
}
function __blShortest(v, single) {
    const sp = __blSpecial(v); if (sp !== null) return sp;
    if (v === 0) return Object.is(v, -0) ? ""-0"" : ""0"";
    let sci;
    if (single) {
        for (let p = 1; p <= 9; p++) { sci = v.toExponential(p - 1); if (Math.fround(Number(sci)) === v) break; }
    } else sci = v.toExponential();
    const m = /^(-?)(\d)(?:\.(\d+))?e([+-]\d+)$/.exec(sci);
    const sign = m[1], digits = m[2] + (m[3] || """"), exp = Number(m[4]), n = digits.length;
    if (exp >= (single ? 9 : 17) || exp < -4) {
        const a = Math.abs(exp);
        return sign + digits[0] + (n > 1 ? ""."" + digits.slice(1) : """") + (exp < 0 ? ""E-"" : ""E+"") + (a < 10 ? ""0"" : """") + a;
    }
    if (exp < 0) return sign + ""0."" + ""0"".repeat(-exp - 1) + digits;
    if (n <= exp + 1) return sign + digits + ""0"".repeat(exp + 1 - n);
    return sign + digits.slice(0, exp + 1) + ""."" + digits.slice(exp + 1);
}
function __blText(v, kind) {
    switch (kind) {
        case ""b"": return v ? ""True"" : ""False"";
        case ""f64"": return __blShortest(v, false);
        case ""f32"": return __blShortest(v, true);
        case ""s"": return v === null || v === undefined ? """" : v;
        default: return v === null || v === undefined ? """" : String(v);
    }
}
function __blBits(kind) { return { i8: 8, u8: 8, i16: 16, u16: 16, i32: 32, u32: 32 }[kind]; }
function __blUnsupported(spec) {
    throw new Error(""String.Format: the format specifier '"" + spec + ""' is not supported on the JavaScript backend (D, X, F, N, E and G are; C and P depend on the culture)"");
}
function __blApply(v, kind, spec) {
    if (spec === """") return __blText(v, kind);
    if (kind === ""b"" || kind === ""s"") return __blText(v, kind);
    if (kind === ""o"") __blUnsupported(spec);
    const integral = __blBits(kind) !== undefined;
    const letter = spec[0];
    const rest = spec.slice(1);
    if (!/^\d{0,3}$/.test(rest)) __blUnsupported(spec);
    const hasPrec = rest.length > 0, prec = hasPrec ? Number(rest) : 0;
    switch (letter) {
        case ""D"": case ""d"": {
            if (!integral) throw new FormatException(""Format specifier was invalid."");
            const neg = v < 0;
            return (neg ? ""-"" : """") + String(Math.abs(v)).padStart(prec, ""0"");
        }
        case ""X"": case ""x"": {
            if (!integral) throw new FormatException(""Format specifier was invalid."");
            const bits = __blBits(kind);
            let u = BigInt(v);
            if (u < 0n) u += 1n << BigInt(bits);
            let hex = u.toString(16);
            if (letter === ""X"") hex = hex.toUpperCase();
            return hex.padStart(prec, ""0"");
        }
        case ""F"": case ""f"": case ""N"": case ""n"": {
            const decimals = hasPrec ? prec : 2, group = letter === ""N"" || letter === ""n"";
            if (integral) {
                const digits = String(Math.abs(v));
                return (v < 0 ? ""-"" : """") + (group ? __blGroup(digits) : digits) + (decimals > 0 ? ""."" + ""0"".repeat(decimals) : """");
            }
            return __blFixed(kind === ""f32"" ? Math.fround(v) : v, decimals, group);
        }
        case ""E"": case ""e"": return __blExp(v, hasPrec ? prec : 6, letter);
        case ""G"": case ""g"": if (hasPrec) __blUnsupported(spec); return __blText(v, kind);
        default: __blUnsupported(spec);
    }
}
function __blFormat(fmt, kinds, ...args) {
    let out = """", k = 0;
    const n = fmt.length;
    const bad = () => { throw new FormatException(""Input string was not in a correct format.""); };
    while (k < n) {
        const c = fmt[k];
        if (c === ""}"") { if (fmt[k + 1] === ""}"") { out += ""}""; k += 2; continue; } bad(); }
        if (c !== ""{"") { out += c; k++; continue; }
        if (fmt[k + 1] === ""{"") { out += ""{""; k += 2; continue; }
        k++;
        const m = /^(\d+) *(?:, *(-?\d+) *)?(?::([^{}]*))?}/.exec(fmt.slice(k));
        if (!m) bad();
        k += m[0].length;
        const index = Number(m[1]);
        if (index >= args.length)
            throw new FormatException(""Index (zero based) must be greater than or equal to zero and less than the size of the argument list."");
        const text = __blApply(args[index], kinds[index], m[3] === undefined ? """" : m[3]);
        const align = m[2] === undefined ? 0 : Number(m[2]);
        out += align < 0 ? text.padEnd(-align) : text.padStart(align);
    }
    return out;
}
function __blParseInt(s, min, max, name) {
    const t = String(s).trim();
    if (!/^[+-]?\d+$/.test(t)) throw new FormatException(""The input string '"" + s + ""' was not in a correct format."");
    const big = BigInt(t);
    if (big < BigInt(min) || big > BigInt(max)) throw new OverflowException(""Value was either too large or too small for "" + name + ""."");
    return Number(big);
}
function __blParseFloat(s) {
    const t = String(s).trim();
    const lower = t.toLowerCase();
    if (lower === ""nan"" || lower === ""+nan"" || lower === ""-nan"") return NaN;
    if (lower === ""infinity"" || lower === ""+infinity"" || lower === ""∞"" || lower === ""+∞"") return Infinity;
    if (lower === ""-infinity"" || lower === ""-∞"") return -Infinity;
    if (!/^[+-]?(\d[\d,]*)?(\.\d*)?([eE][+-]?\d+)?$/.test(t) || !/\d/.test(t.replace(/[eE].*$/, """")))
        throw new FormatException(""The input string '"" + s + ""' was not in a correct format."");
    return Number(t.replace(/,/g, """"));
}
function __blParseBool(s) {
    const t = String(s).trim().toLowerCase();
    if (t === ""true"") return true;
    if (t === ""false"") return false;
    throw new FormatException(""String '"" + s + ""' was not recognized as a valid Boolean."");
}
";

        /// <summary>The exception classes the prelude throws; CollectRequired adds them when used.</summary>
        public static readonly string[] ThrownExceptions = { "FormatException", "OverflowException" };

        /// <summary>True when the module uses any row (the scan is shared with the C++ splice).</summary>
        public static bool IsUsed(IRModule module) => PrimitiveStaticSurface.IsUsedBy(module);

        /// <summary>The JS type kind of a format argument (see __blText / __blApply).</summary>
        private static string Kind(TypeInfo type) => type?.Name?.ToLowerInvariant() switch
        {
            "integer" => "i32",
            "short" => "i16",
            "sbyte" => "i8",
            "byte" or "ubyte" => "u8",
            "ushort" => "u16",
            "uinteger" => "u32",
            "double" => "f64",
            "single" => "f32",
            "boolean" => "b",
            "string" => "s",
            _ => "o",
        };

        /// <summary>
        /// The JS for a row called as <c>Type.Member(args)</c>, or false when <paramref name="name"/> is not
        /// a supported row (the caller refuses it).
        /// </summary>
        public static bool TryLowerCall(string name, IReadOnlyList<IRValue> args, IReadOnlyList<string> rendered,
            out string js)
        {
            js = null;
            if (!PrimitiveStaticSurface.TryGetDotted(name, out var row)) return false;
            if (row.IsProperty) return TryLowerProperty(row, out js);
            if (!row.AcceptsArgCount(args.Count)) return false;

            var type = row.TypeName;
            var member = row.MemberName;
            string A(int i) => rendered[i];
            // The text of one value as .NET writes it.
            string Text(int i) => $"__blText({A(i)}, \"{Kind(args[i].Type)}\")";

            switch (type, member)
            {
                case ("String", "Format"):
                    var kinds = string.Join(", ", args.Skip(1).Select(a => $"\"{Kind(a.Type)}\""));
                    js = $"__blFormat({string.Join(", ", new[] { A(0), $"[{kinds}]" }.Concat(rendered.Skip(1)))})";
                    return true;
                case ("String", "Join"):
                    if (args.Count == 2 && args[1].Type?.Kind == TypeKind.Array || args.Count == 2 && IsListType(args[1].Type))
                    {
                        var element = args[1].Type?.ElementType ?? args[1].Type?.GenericArguments?.FirstOrDefault();
                        var elementText = Kind(element) switch
                        {
                            "b" => "(x ? \"True\" : \"False\")",
                            "f64" => "__blShortest(x, false)",
                            "f32" => "__blShortest(x, true)",
                            _ => "(x ?? \"\")",
                        };
                        js = $"{A(1)}.map(x => {elementText}).join({A(0)})";
                        return true;
                    }
                    js = $"[{string.Join(", ", Enumerable.Range(1, args.Count - 1).Select(Text))}].join({A(0)})";
                    return true;
                case ("String", "Concat"):
                    js = args.Count == 0 ? "\"\"" : $"({string.Join(" + ", new[] { "\"\"" }.Concat(Enumerable.Range(0, args.Count).Select(Text)))})";
                    return true;
                case ("String", "IsNullOrEmpty"):
                    js = $"(({A(0)} ?? \"\") === \"\")";
                    return true;
                case ("String", "IsNullOrWhiteSpace"):
                    js = $"/^\\s*$/.test({A(0)} ?? \"\")";
                    return true;
                case ("Boolean", "Parse"):
                    js = $"__blParseBool({A(0)})";
                    return true;
                case (_, "Parse") when type is "Double":
                    js = $"__blParseFloat({A(0)})";
                    return true;
                case (_, "Parse") when type is "Single":
                    js = $"Math.fround(__blParseFloat({A(0)}))";
                    return true;
                case (_, "Parse") when IntegralRange(type) is var (min, max, phrase) && phrase != null:
                    js = $"__blParseInt({A(0)}, \"{min}\", \"{max}\", \"{phrase}\")";
                    return true;
                case (_, "IsNaN"):
                    js = $"Number.isNaN({A(0)})";
                    return true;
                case (_, "IsInfinity"):
                    js = $"(Math.abs({A(0)}) === Infinity)";
                    return true;
            }
            return false;
        }

        /// <summary>The JS for a row read as a property (<c>Integer.MaxValue</c>, <c>String.Empty</c>).</summary>
        public static bool TryLowerProperty(PrimitiveStaticSurface.Row row, out string js)
        {
            js = null;
            if (!row.IsProperty) return false;
            var type = row.TypeName;
            if (row.MemberName == "Empty") { js = "\"\""; return true; }

            if (IntegralRange(type) is var (min, max, phrase) && phrase != null)
            {
                js = row.MemberName == "MaxValue" ? max : row.MemberName == "MinValue" ? $"({min})" : null;
                return js != null;
            }

            var single = type == "Single";
            js = row.MemberName switch
            {
                "MaxValue" => single ? "3.4028234663852886e+38" : "Number.MAX_VALUE",
                "MinValue" => single ? "(-3.4028234663852886e+38)" : "(-Number.MAX_VALUE)",
                "Epsilon" => single ? "1.401298464324817e-45" : "Number.MIN_VALUE",
                "NaN" => "NaN",
                "PositiveInfinity" => "Infinity",
                "NegativeInfinity" => "(-Infinity)",
                _ => null,
            };
            return js != null;
        }

        private static bool IsListType(TypeInfo type) =>
            string.Equals(type?.Name, "List", StringComparison.OrdinalIgnoreCase);

        /// <summary>(min, max, overflow phrase) of the 32-bit-or-narrower integral keywords; Long/ULong are refused on JS.</summary>
        private static (string Min, string Max, string Phrase) IntegralRange(string type) => type switch
        {
            "Integer" => ("-2147483648", "2147483647", "an Int32"),
            "Short" => ("-32768", "32767", "an Int16"),
            "Byte" => ("0", "255", "an unsigned byte"),
            "UShort" => ("0", "65535", "a UInt16"),
            "UInteger" => ("0", "4294967295", "a UInt32"),
            _ => (null, null, null),
        };
    }
}
