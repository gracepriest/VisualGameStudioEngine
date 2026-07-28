using BasicLang.Compiler.CodeGen.CPlusPlus;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Tests for the native BCL value-type runtime header <c>bl_bcltypes.hpp</c>
/// (<see cref="CppBclRuntime"/>, spec 2026-07-27-p1-native-bcl-types §3/§9/§12).
///
/// FAST section (no category): content pins on the header text — the spec §12 layer-1
/// guard that runs without a C++ compiler.
///
/// INTEGRATION section (<c>[Category("Integration")]</c> per test): compiles the header
/// with a real C++ compiler via <see cref="Native.CppCompile"/> and runs the plan's
/// vector battery — marker-style programs printing value-independent comparisons
/// (<c>name=%d</c>), asserted as <c>Does.Contain("name=1")</c> so the C# side never
/// hard-codes platform values. Every expected value was verified against real .NET
/// (PowerShell <c>[DateTime]</c>/<c>[TimeSpan]</c> oracle).
/// </summary>
[TestFixture]
public class CppBclRuntimeTests
{
    // ---------------- FAST: content pins (spec §12 layer 1) ----------------

    [Test]
    public void BclHeader_ContainsSourceOfTruthBanner() =>
        Assert.That(CppBclRuntime.BclHeader,
            Does.Contain("SOURCE OF TRUTH: BasicLang CppBclRuntime.cs"));

    [Test]
    public void BclHeader_DefinesTimeSpanStruct() =>
        Assert.That(CppBclRuntime.BclHeader, Does.Contain("struct TimeSpan"));

    [Test]
    public void BclHeader_DefinesDateTimeStruct() =>
        Assert.That(CppBclRuntime.BclHeader, Does.Contain("struct DateTime"));

    [Test]
    public void BclHeader_DefinesGuidStruct() =>
        Assert.That(CppBclRuntime.BclHeader, Does.Contain("struct Guid"));

    [Test]
    public void BclHeader_DefinesDateTimeOffsetStruct() =>
        Assert.That(CppBclRuntime.BclHeader, Does.Contain("struct DateTimeOffset"));

    /// <summary>StringBuilder is the ONE reference type (spec §3): shared_ptr + enable_shared_from_this.</summary>
    [Test]
    public void BclHeader_DefinesStringBuilderReferenceType() =>
        Assert.That(CppBclRuntime.BclHeader,
            Does.Contain("class StringBuilder : public std::enable_shared_from_this<StringBuilder>"));

    /// <summary>
    /// Spec §3: NewGuid entropy comes from the OS CSPRNG — never rand()/mt19937.
    /// (Comments may NAME the banned APIs; the pins reject their usable spellings.)
    /// </summary>
    [Test]
    public void BclHeader_NewGuidUsesOsCsprng()
    {
        Assert.That(CppBclRuntime.BclHeader, Does.Contain("BCryptGenRandom"));
        Assert.That(CppBclRuntime.BclHeader, Does.Not.Contain("std::mt19937"));
        Assert.That(CppBclRuntime.BclHeader, Does.Not.Contain("std::rand"));
        Assert.That(CppBclRuntime.BclHeader, Does.Not.Contain("random_device"));
    }

    /// <summary>Task 7 rider (3): the unused &lt;chrono&gt; include is gone.</summary>
    [Test]
    public void BclHeader_DoesNotIncludeChrono() =>
        Assert.That(CppBclRuntime.BclHeader, Does.Not.Contain("<chrono>"));

    [Test]
    public void BclHeader_DefinesHashSpecializations()
    {
        Assert.That(CppBclRuntime.BclHeader, Does.Contain("std::hash<BasicLang::DateTime>"));
        Assert.That(CppBclRuntime.BclHeader, Does.Contain("std::hash<BasicLang::TimeSpan>"));
        Assert.That(CppBclRuntime.BclHeader, Does.Contain("std::hash<BasicLang::Guid>"));
        Assert.That(CppBclRuntime.BclHeader, Does.Contain("std::hash<BasicLang::DateTimeOffset>"));
    }

    [Test]
    public void BclHeader_DefinesOstreamInserters()
    {
        Assert.That(CppBclRuntime.BclHeader,
            Does.Contain("operator<<(std::ostream& os, const TimeSpan& v)"));
        Assert.That(CppBclRuntime.BclHeader,
            Does.Contain("operator<<(std::ostream& os, const DateTime& v)"));
        Assert.That(CppBclRuntime.BclHeader,
            Does.Contain("operator<<(std::ostream& os, const Guid& v)"));
        Assert.That(CppBclRuntime.BclHeader,
            Does.Contain("operator<<(std::ostream& os, const DateTimeOffset& v)"));
        Assert.That(CppBclRuntime.BclHeader,
            Does.Contain("operator<<(std::ostream& os, const std::shared_ptr<StringBuilder>& sb)"));
    }

    /// <summary>
    /// Task 9 splice preconditions: the Body piece is include-free (std headers are
    /// generator-owned), opens its own <c>namespace BasicLang</c>, and the whole-file
    /// property is exactly Includes + Body.
    /// </summary>
    [Test]
    public void BclBody_IsIncludeFreeAndOpensNamespace()
    {
        Assert.That(CppBclRuntime.BclBody, Does.Not.Contain("#include"));
        Assert.That(CppBclRuntime.BclBody, Does.Not.Contain("#pragma once"));
        Assert.That(CppBclRuntime.BclBody.TrimStart(), Does.StartWith("namespace BasicLang {"));
        Assert.That(CppBclRuntime.BclHeader,
            Is.EqualTo(CppBclRuntime.BclIncludes + CppBclRuntime.BclBody));
    }

    // ---------------- INTEGRATION: native vector battery ----------------

    private (string exe, string argsTemplate)? _compiler;

    [OneTimeSetUp]
    public void FindCompiler() => _compiler = Native.CppCompile.FindRunCompiler();

    /// <summary>
    /// Compile <c>#include "bl_bcltypes.hpp"</c> + <paramref name="mainBody"/> with the
    /// probed compiler (header passed via extraFiles) and return the program's stdout.
    /// </summary>
    private string Run(string mainBody)
    {
        if (_compiler is null) Assert.Ignore("No C++ compiler available");
        var src = "#include \"bl_bcltypes.hpp\"\n#include <cstdio>\n" + mainBody;
        return Native.CppCompile.CompileAndRun(src, _compiler!.Value, new Dictionary<string, string>
        {
            ["bl_bcltypes.hpp"] = CppBclRuntime.BclHeader,
        });
    }

    /// <summary>Asserts every marker printed by the program came out as <c>name=1</c>.</summary>
    private static void AssertMarkers(string output, params string[] markers)
    {
        foreach (var m in markers)
            Assert.That(output, Does.Contain(m + "=1"), $"marker '{m}' not 1; full output:\n{output}");
    }

    [Test, Category("Integration")]
    public void HeaderCompilesStandalone() =>
        Assert.That(Run("int main(){ printf(\"ok\"); return 0; }"), Is.EqualTo("ok"));

    /// <summary>Gregorian calendar rules: leap years (incl. 1900/2000), DaysInMonth, ctor validation, DayOfWeek, DayOfYear.</summary>
    [Test, Category("Integration")]
    public void Calendar_LeapYears_DaysInMonth_DayOfWeek_DayOfYear()
    {
        var output = Run(@"
using namespace BasicLang;
int main() {
    DateTime feb29(2024, 2, 29);
    printf(""feb29_ok=%d\n"", feb29.Day() == 29);
    int threw = 0;
    try { DateTime bad(2023, 2, 29); (void)bad; } catch (const std::runtime_error&) { threw = 1; }
    printf(""feb29_2023_throws=%d\n"", threw);
    threw = 0;
    try { DateTime bad(2026, 13, 1); (void)bad; } catch (const std::runtime_error&) { threw = 1; }
    printf(""month13_throws=%d\n"", threw);
    printf(""dim_feb_2024=%d\n"", DateTime::DaysInMonth(2024, 2) == 29);
    printf(""dim_feb_2023=%d\n"", DateTime::DaysInMonth(2023, 2) == 28);
    printf(""dim_apr=%d\n"", DateTime::DaysInMonth(2026, 4) == 30);
    printf(""leap_1900=%d\n"", DateTime::IsLeapYear(1900) == false);
    printf(""leap_2000=%d\n"", DateTime::IsLeapYear(2000) == true);
    printf(""leap_2024=%d\n"", DateTime::IsLeapYear(2024) == true);
    printf(""dow_sun=%d\n"", DateTime(2026, 7, 26).DayOfWeek() == 0); /* Sunday — verified real .NET */
    printf(""doy_1=%d\n"", DateTime(2026, 1, 1).DayOfYear() == 1);
    printf(""doy_365=%d\n"", DateTime(2026, 12, 31).DayOfYear() == 365);
    return 0;
}
");
        AssertMarkers(output,
            "feb29_ok", "feb29_2023_throws", "month13_throws",
            "dim_feb_2024", "dim_feb_2023", "dim_apr",
            "leap_1900", "leap_2000", "leap_2024",
            "dow_sun", "doy_1", "doy_365");
    }

    /// <summary>Ticks→components round-trip for the extremes and known epochs (ticks values verified against real .NET).</summary>
    [Test, Category("Integration")]
    public void RoundTrip_TicksToComponents_AcrossRange()
    {
        var output = Run(@"
using namespace BasicLang;
static int check(int y, int mo, int d, int h, int mi, int s, int kind) {
    DateTime a(y, mo, d, h, mi, s);
    DateTime b = DateTime::FromTicksAndKind(a.Ticks(), kind);
    return b.Year() == y && b.Month() == mo && b.Day() == d
        && b.Hour() == h && b.Minute() == mi && b.Second() == s && b.Kind() == kind;
}
int main() {
    printf(""min=%d\n"", check(1, 1, 1, 0, 0, 0, DateTime::KindUnspecified));
    printf(""max=%d\n"", check(9999, 12, 31, 23, 59, 59, DateTime::KindUtc));
    printf(""epoch=%d\n"", check(1970, 1, 1, 0, 0, 0, DateTime::KindLocal));
    printf(""leapday=%d\n"", check(2000, 2, 29, 12, 30, 45, DateTime::KindUnspecified));
    printf(""epoch_ticks=%d\n"", DateTime(1970, 1, 1).Ticks() == 621355968000000000LL);
    printf(""min_ticks=%d\n"", DateTime(1, 1, 1).Ticks() == 0);
    printf(""max_ticks=%d\n"", DateTime(9999, 12, 31, 23, 59, 59).Ticks() == 3155378975990000000LL);
    return 0;
}
");
        AssertMarkers(output, "min", "max", "epoch", "leapday", "epoch_ticks", "min_ticks", "max_ticks");
    }

    /// <summary>AddMonths day clamping (Jan 31 + 1mo → Feb 28/29), negative months, AddYears off a leap day.</summary>
    [Test, Category("Integration")]
    public void AddMonths_ClampsDayToTargetMonth()
    {
        var output = Run(@"
using namespace BasicLang;
int main() {
    DateTime a = DateTime(2026, 1, 31).AddMonths(1);
    printf(""clamp2026=%d\n"", a.Year() == 2026 && a.Month() == 2 && a.Day() == 28);
    DateTime b = DateTime(2024, 1, 31).AddMonths(1);
    printf(""clamp2024=%d\n"", b.Year() == 2024 && b.Month() == 2 && b.Day() == 29);
    DateTime c = DateTime(2026, 3, 15, 10, 20, 30).AddMonths(13);
    printf(""advance=%d\n"", c.Year() == 2027 && c.Month() == 4 && c.Day() == 15 && c.Hour() == 10);
    DateTime d = DateTime(2026, 3, 31).AddMonths(-1);
    printf(""clampneg=%d\n"", d.Year() == 2026 && d.Month() == 2 && d.Day() == 28);
    DateTime e = DateTime(2024, 2, 29).AddYears(1);
    printf(""addyears_clamp=%d\n"", e.Year() == 2025 && e.Month() == 2 && e.Day() == 28);
    return 0;
}
");
        AssertMarkers(output, "clamp2026", "clamp2024", "advance", "clampneg", "addyears_clamp");
    }

    /// <summary>dt−dt → TimeSpan, dt±ts, and ticks-only comparisons across Kind (Kind is metadata).</summary>
    [Test, Category("Integration")]
    public void Arithmetic_DateDiff_AddTimeSpan_TicksOnlyComparison()
    {
        var output = Run(@"
using namespace BasicLang;
int main() {
    DateTime d1(2026, 1, 1), d2(2026, 1, 31);
    printf(""days30=%d\n"", (d2 - d1).Days() == 30);
    DateTime n = d1 + TimeSpan::FromDays(1.0);
    printf(""nextday=%d\n"", n.Month() == 1 && n.Day() == 2);
    DateTime back = n - TimeSpan::FromDays(1.0);
    printf(""subtract_ts=%d\n"", back == d1);
    /* ticks-only comparison across Kind (spec: Kind is metadata) */
    DateTime utc = DateTime::FromTicksAndKind(d1.Ticks(), DateTime::KindUtc);
    DateTime loc = DateTime::FromTicksAndKind(d1.Ticks(), DateTime::KindLocal);
    printf(""kind_eq=%d\n"", utc == loc);
    printf(""kind_le_ge=%d\n"", utc <= loc && utc >= loc);
    printf(""kind_ne0=%d\n"", (utc != loc) == false);
    DateTime later = DateTime::FromTicksAndKind(d2.Ticks(), DateTime::KindUtc);
    printf(""lt_cross_kind=%d\n"", loc < later && later > loc);
    printf(""compareto=%d\n"", d1.CompareTo(d2) == -1 && d2.CompareTo(d1) == 1 && d1.CompareTo(utc) == 0);
    return 0;
}
");
        AssertMarkers(output,
            "days30", "nextday", "subtract_ts",
            "kind_eq", "kind_le_ge", "kind_ne0", "lt_cross_kind", "compareto");
    }

    /// <summary>TimeSpan components vs totals (sign included), millisecond-rounding factories, operators.</summary>
    [Test, Category("Integration")]
    public void TimeSpan_ComponentsVsTotals_MillisecondRounding()
    {
        var output = Run(@"
using namespace BasicLang;
int main() {
    TimeSpan t = TimeSpan::FromSeconds(90.0);
    printf(""min1=%d\n"", t.Minutes() == 1);
    printf(""sec30=%d\n"", t.Seconds() == 30);
    printf(""total15=%d\n"", t.TotalMinutes() == 1.5);
    TimeSpan neg = TimeSpan::FromSeconds(-90.0);
    printf(""negmin=%d\n"", neg.Minutes() == -1);
    printf(""negsec=%d\n"", neg.Seconds() == -30);
    printf(""negtotal=%d\n"", neg.TotalMinutes() == -1.5);
    TimeSpan c(1, 2, 3);
    printf(""hms=%d\n"", c.Hours() == 1 && c.Minutes() == 2 && c.Seconds() == 3);
    TimeSpan d(1, 1, 2, 3);
    printf(""dhms=%d\n"", d.Days() == 1 && d.Hours() == 1 && d.Minutes() == 2 && d.Seconds() == 3);
    /* double factories round to the nearest MILLISECOND (.NET rule) */
    printf(""round0=%d\n"", TimeSpan::FromSeconds(0.0001).Ticks() == 0);
    printf(""fromms1=%d\n"", TimeSpan::FromMilliseconds(1.0).Ticks() == 10000);
    printf(""fromdays1=%d\n"", TimeSpan::FromDays(1.0).Ticks() == 864000000000LL);
    /* arithmetic + Duration + CompareTo + relational operators */
    printf(""add=%d\n"", (c + d).Ticks() == c.Ticks() + d.Ticks());
    printf(""sub=%d\n"", (d - c).Days() == 1 && (d - c).Hours() == 0);
    printf(""duration=%d\n"", neg.Duration() == t);
    printf(""cmp=%d\n"", c.CompareTo(d) == -1 && d.CompareTo(c) == 1 && c.CompareTo(c) == 0);
    printf(""relops=%d\n"", c < d && d > c && c <= c && c >= c && c != d);
    return 0;
}
");
        AssertMarkers(output,
            "min1", "sec30", "total15", "negmin", "negsec", "negtotal",
            "hms", "dhms", "round0", "fromms1", "fromdays1",
            "add", "sub", "duration", "cmp", "relops");
    }

    /// <summary>Token ToString (G default, yyyy-MM-dd, O, s, fff) and Parse round-trips; TimeSpan "c" both ways.</summary>
    [Test, Category("Integration")]
    public void ToStringAndParse_InvariantFormats_RoundTrip()
    {
        var output = Run(@"
using namespace BasicLang;
int main() {
    DateTime d(2026, 7, 26, 13, 5, 9);
    printf(""g=%d\n"", d.ToString() == ""07/26/2026 13:05:09"");
    printf(""ymd=%d\n"", d.ToString(""yyyy-MM-dd"") == ""2026-07-26"");
    printf(""o=%d\n"", d.ToString(""O"") == ""2026-07-26T13:05:09.0000000"");
    printf(""s=%d\n"", d.ToString(""s"") == ""2026-07-26T13:05:09"");
    printf(""fff=%d\n"", d.AddMilliseconds(500.0).ToString(""HH:mm:ss.fff"") == ""13:05:09.500"");
    printf(""parse_g=%d\n"", DateTime::Parse(""07/26/2026 13:05:09"") == d);
    printf(""parse_ymd=%d\n"", DateTime::Parse(""2026-07-26"") == DateTime(2026, 7, 26));
    printf(""parse_o=%d\n"", DateTime::Parse(""2026-07-26T13:05:09.0000000"") == d);
    printf(""parse_s=%d\n"", DateTime::Parse(""2026-07-26T13:05:09"") == d);
    TimeSpan t(1, 2, 3);
    printf(""ts_c=%d\n"", t.ToString() == ""01:02:03"");
    TimeSpan t2(1, 1, 2, 3);
    printf(""ts_cd=%d\n"", t2.ToString() == ""1.01:02:03"");
    printf(""ts_neg=%d\n"", (-t2).ToString() == ""-1.01:02:03"");
    printf(""ts_frac=%d\n"", TimeSpan::FromMilliseconds(1.0).ToString() == ""00:00:00.0010000"");
    printf(""ts_parse=%d\n"", TimeSpan::Parse(""1.01:02:03"") == t2);
    printf(""ts_parse_neg=%d\n"", TimeSpan::Parse(""-01:02:03"") == -TimeSpan(1, 2, 3));
    printf(""ts_parse_frac=%d\n"", TimeSpan::Parse(""00:00:00.0010000"").Ticks() == 10000);
    return 0;
}
");
        AssertMarkers(output,
            "g", "ymd", "o", "s", "fff",
            "parse_g", "parse_ymd", "parse_o", "parse_s",
            "ts_c", "ts_cd", "ts_neg", "ts_frac",
            "ts_parse", "ts_parse_neg", "ts_parse_frac");
    }

    /// <summary>ostream inserters equal ToString; std::hash is ticks-only (equal across Kind), matching equality.</summary>
    [Test, Category("Integration")]
    public void OstreamInserters_And_HashEqualAcrossKind()
    {
        var output = Run(@"
#include <sstream>
using namespace BasicLang;
int main() {
    DateTime d(2026, 7, 26, 13, 5, 9);
    std::ostringstream oss;
    oss << d;
    printf(""dt_stream=%d\n"", oss.str() == d.ToString());
    TimeSpan t(1, 2, 3);
    std::ostringstream ost;
    ost << t;
    printf(""ts_stream=%d\n"", ost.str() == t.ToString());
    /* hash over ticks only: equal across Kind, matching equality (spec 6.2) */
    DateTime u = DateTime::FromTicksAndKind(d.Ticks(), DateTime::KindUtc);
    printf(""hash_kind=%d\n"", std::hash<DateTime>{}(d) == std::hash<DateTime>{}(u));
    printf(""ts_hash=%d\n"", std::hash<TimeSpan>{}(t) == std::hash<TimeSpan>{}(TimeSpan::FromTicks(t.Ticks())));
    return 0;
}
");
        AssertMarkers(output, "dt_stream", "ts_stream", "hash_kind", "ts_hash");
    }

    /// <summary>Overflow and bad-input paths all throw std::runtime_error (spec §11 — BL catch reachability).</summary>
    [Test, Category("Integration")]
    public void Overflow_And_BadParse_ThrowRuntimeError()
    {
        var output = Run(@"
using namespace BasicLang;
int main() {
    int threw = 0;
    try { DateTime::MaxValue().AddDays(1.0); } catch (const std::runtime_error&) { threw = 1; }
    printf(""max_adddays=%d\n"", threw);
    threw = 0;
    try { TimeSpan::MinValue().Negate(); } catch (const std::runtime_error&) { threw = 1; }
    printf(""ts_negate_min=%d\n"", threw);
    threw = 0;
    try { TimeSpan::MaxValue().Add(TimeSpan::FromTicks(1)); } catch (const std::runtime_error&) { threw = 1; }
    printf(""ts_add_over=%d\n"", threw);
    threw = 0;
    try { DateTime::MinValue().AddTicks(-1); } catch (const std::runtime_error&) { threw = 1; }
    printf(""min_addticks=%d\n"", threw);
    threw = 0;
    try { DateTime::Parse(""garbage""); } catch (const std::runtime_error&) { threw = 1; }
    printf(""parse_bad=%d\n"", threw);
    threw = 0;
    try { TimeSpan::Parse(""nope""); } catch (const std::runtime_error&) { threw = 1; }
    printf(""ts_parse_bad=%d\n"", threw);
    return 0;
}
");
        AssertMarkers(output,
            "max_adddays", "ts_negate_min", "ts_add_over",
            "min_addticks", "parse_bad", "ts_parse_bad");
    }

    /// <summary>
    /// OS-backed local time: Now/UtcNow/Today Kinds, tick-stable UTC↔local round-trip for a
    /// contemporary date, and the spec §9 rule that pre-1970 conversions THROW (message
    /// contains "range") rather than clamp.
    /// </summary>
    [Test, Category("Integration")]
    public void LocalTime_Kinds_RoundTripStable_Pre1970Throws()
    {
        var output = Run(@"
using namespace BasicLang;
int main() {
    printf(""now_kind=%d\n"", DateTime::Now().Kind() == DateTime::KindLocal);
    printf(""utcnow_kind=%d\n"", DateTime::UtcNow().Kind() == DateTime::KindUtc);
    DateTime today = DateTime::Today();
    printf(""today_kind=%d\n"", today.Kind() == DateTime::KindLocal);
    printf(""today_midnight=%d\n"", today.Hour() == 0 && today.Minute() == 0 && today.Second() == 0);
    /* contemporary round-trip: (assumed-UTC) -> local -> back to UTC must be tick-stable */
    DateTime x(2026, 7, 15, 12, 0, 0);
    DateTime loc = x.ToLocalTime();
    printf(""loc_kind=%d\n"", loc.Kind() == DateTime::KindLocal);
    DateTime back = loc.ToUniversalTime();
    printf(""rt_stable=%d\n"", back.Ticks() == x.Ticks());
    printf(""rt_kind=%d\n"", back.Kind() == DateTime::KindUtc);
    /* already-converted values are returned unchanged */
    printf(""loc_idem=%d\n"", loc.ToLocalTime().Ticks() == loc.Ticks());
    printf(""utc_idem=%d\n"", back.ToUniversalTime().Ticks() == back.Ticks());
    /* pre-1970 local conversion THROWS with ""range"" in the message (spec 9) */
    int threw = 0;
    std::string msg;
    try { DateTime(1950, 1, 1).ToLocalTime(); } catch (const std::runtime_error& e) { threw = 1; msg = e.what(); }
    printf(""pre1970_throws=%d\n"", threw);
    printf(""msg_range=%d\n"", msg.find(""range"") != std::string::npos);
    return 0;
}
");
        AssertMarkers(output,
            "now_kind", "utcnow_kind", "today_kind", "today_midnight",
            "loc_kind", "rt_stable", "rt_kind", "loc_idem", "utc_idem",
            "pre1970_throws", "msg_range");
    }

    /// <summary>
    /// Guid battery (spec §3/§5/§8): CSPRNG v4 NewGuid (version/variant nibbles), D/N/B/P
    /// Parse+ToString round-trips, the §8 mixed-endian ToByteArray pin, Empty, field-order
    /// CompareTo (NOT byte-array order), stream inserter, hash. All pinned values verified
    /// against real .NET ([Guid] oracle).
    /// </summary>
    [Test, Category("Integration")]
    public void Guid_NewGuid_ParseFormats_ByteOrder_CompareTo()
    {
        var output = Run(@"
#include <sstream>
using namespace BasicLang;
int main() {
    Guid g1 = Guid::NewGuid(), g2 = Guid::NewGuid();
    printf(""unique=%d\n"", !(g1 == g2));
    std::string s1 = g1.ToString();
    printf(""shape=%d\n"", s1.size() == 36 && s1[8] == '-' && s1[13] == '-' && s1[18] == '-' && s1[23] == '-');
    int lower = 1;
    for (size_t i = 0; i < s1.size(); ++i) {
        if (i == 8 || i == 13 || i == 18 || i == 23) continue;
        char c = s1[i];
        if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) lower = 0;
    }
    printf(""lowerhex=%d\n"", lower);
    printf(""version4=%d\n"", s1[14] == '4' && g2.ToString()[14] == '4');
    char v1 = s1[19], v2 = g2.ToString()[19];
    printf(""variant=%d\n"", (v1=='8'||v1=='9'||v1=='a'||v1=='b') && (v2=='8'||v2=='9'||v2=='a'||v2=='b'));
    printf(""rt=%d\n"", Guid::Parse(s1) == g1);
    /* String ctor (spec 5) == Parse */
    printf(""ctor=%d\n"", Guid(""00112233-4455-6677-8899-aabbccddeeff"") == Guid::Parse(""00112233-4455-6677-8899-aabbccddeeff""));
    Guid p = Guid::Parse(""00112233-4455-6677-8899-aabbccddeeff"");
    printf(""d_fmt=%d\n"", p.ToString() == ""00112233-4455-6677-8899-aabbccddeeff"");
    printf(""n_fmt=%d\n"", p.ToString(""N"") == ""00112233445566778899aabbccddeeff"");
    printf(""b_fmt=%d\n"", p.ToString(""B"") == ""{00112233-4455-6677-8899-aabbccddeeff}"");
    printf(""p_fmt=%d\n"", p.ToString(""P"") == ""(00112233-4455-6677-8899-aabbccddeeff)"");
    printf(""parse_n=%d\n"", Guid::Parse(""00112233445566778899aabbccddeeff"") == p);
    printf(""parse_b=%d\n"", Guid::Parse(""{00112233-4455-6677-8899-aabbccddeeff}"") == p);
    printf(""parse_p=%d\n"", Guid::Parse(""(00112233-4455-6677-8899-aabbccddeeff)"") == p);
    printf(""parse_upper=%d\n"", Guid::Parse(""00112233-4455-6677-8899-AABBCCDDEEFF"") == p);
    /* spec 8 pin: .NET mixed-endian ToByteArray — a,b,c little-endian, d verbatim */
    uint8_t b[16];
    p.ToByteArray(b);
    const uint8_t want[16] = {0x33,0x22,0x11,0x00,0x55,0x44,0x77,0x66,0x88,0x99,0xaa,0xbb,0xcc,0xdd,0xee,0xff};
    printf(""bytes=%d\n"", std::memcmp(b, want, 16) == 0);
    printf(""empty=%d\n"", Guid::Empty().ToString() == ""00000000-0000-0000-0000-000000000000"");
    /* FIELD-order CompareTo (verified .NET: -1 here; memcmp of ToByteArray would say +1) */
    printf(""cmp_a=%d\n"", Guid::Parse(""00000001-0000-0000-0000-000000000000"")
                              .CompareTo(Guid::Parse(""00000100-0000-0000-0000-000000000000"")) == -1);
    printf(""cmp_d=%d\n"", Guid::Parse(""00000000-0000-0000-0100-000000000000"")
                              .CompareTo(Guid::Parse(""00000000-0000-0000-0002-000000000000"")) == 1);
    printf(""cmp_eq=%d\n"", p.CompareTo(Guid(""00112233-4455-6677-8899-aabbccddeeff"")) == 0);
    std::ostringstream oss;
    oss << p;
    printf(""stream=%d\n"", oss.str() == p.ToString());
    printf(""hash_eq=%d\n"", std::hash<Guid>{}(p) == std::hash<Guid>{}(Guid(""00112233-4455-6677-8899-aabbccddeeff"")));
    int threw = 0;
    try { Guid::Parse(""garbage""); } catch (const std::runtime_error&) { threw = 1; }
    printf(""bad_throws=%d\n"", threw);
    threw = 0;
    try { Guid::Parse(""00112233-4455-6677-8899-aabbccddeef""); } catch (const std::runtime_error&) { threw = 1; }
    printf(""short_throws=%d\n"", threw);
    threw = 0;
    try { Guid::Parse(""0011223-34455-6677-8899-aabbccddeeff""); } catch (const std::runtime_error&) { threw = 1; }
    printf(""dashpos_throws=%d\n"", threw);
    return 0;
}
");
        AssertMarkers(output,
            "unique", "shape", "lowerhex", "version4", "variant", "rt", "ctor",
            "d_fmt", "n_fmt", "b_fmt", "p_fmt",
            "parse_n", "parse_b", "parse_p", "parse_upper",
            "bytes", "empty", "cmp_a", "cmp_d", "cmp_eq", "stream", "hash_eq",
            "bad_throws", "short_throws", "dashpos_throws");
    }

    /// <summary>
    /// DateTimeOffset battery (spec §3): UTC-instant equality (the spec's own 10:00+02:00 ==
    /// 09:00+01:00 example), Unix epoch conversions, offset validation throws, instant-preserving
    /// ToOffset, invariant ToString with the zzz suffix, stream inserter, UTC-instant hash across
    /// offsets, and the OS local seam. Pinned values verified against real .NET.
    /// </summary>
    [Test, Category("Integration")]
    public void DateTimeOffset_InstantEquality_UnixTime_ToOffset_OsSeam()
    {
        var output = Run(@"
#include <sstream>
using namespace BasicLang;
int main() {
    /* spec 3's example: equality compares the UTC instant */
    DateTimeOffset a(DateTime(2026, 1, 1, 10, 0, 0), TimeSpan::FromHours(2.0));
    DateTimeOffset b(DateTime(2026, 1, 1, 9, 0, 0), TimeSpan::FromHours(1.0));
    printf(""instant_eq=%d\n"", a == b);
    printf(""cmp0=%d\n"", a.CompareTo(b) == 0);
    printf(""utcdt=%d\n"", a.UtcDateTime() == DateTime(2026, 1, 1, 8, 0, 0));
    printf(""utcdt_kind=%d\n"", a.UtcDateTime().Kind() == DateTime::KindUtc);
    printf(""clockdt=%d\n"", a.ClockDateTime() == DateTime(2026, 1, 1, 10, 0, 0));
    printf(""clock_kind=%d\n"", a.ClockDateTime().Kind() == DateTime::KindUnspecified);
    printf(""ticks_clock=%d\n"", a.TicksValue() == DateTime(2026, 1, 1, 10, 0, 0).Ticks());
    printf(""offset=%d\n"", a.Offset() == TimeSpan::FromHours(2.0));
    /* Unix conversions (verified .NET: 2026-01-01T00:00:00Z == 1767225600) */
    printf(""unix0=%d\n"", DateTimeOffset::FromUnixTimeSeconds(0).UtcDateTime() == DateTime(1970, 1, 1));
    printf(""unix0_off=%d\n"", DateTimeOffset::FromUnixTimeSeconds(0).Offset() == TimeSpan::Zero());
    DateTimeOffset u = DateTimeOffset::FromUnixTimeSeconds(1767225600LL);
    printf(""unix2026=%d\n"", u.UtcDateTime() == DateTime(2026, 1, 1));
    printf(""unix_rt=%d\n"", u.ToUnixTimeSeconds() == 1767225600LL);
    printf(""unixms_rt=%d\n"", u.ToUnixTimeMilliseconds() == 1767225600000LL);
    printf(""unixms_from=%d\n"", DateTimeOffset::FromUnixTimeMilliseconds(1767225600000LL) == u);
    printf(""unix_neg=%d\n"", DateTimeOffset::FromUnixTimeSeconds(-1).ToUnixTimeSeconds() == -1);
    /* offset validation (spec 11): >14h, sub-minute, Utc-kind clock + nonzero offset */
    int threw = 0;
    try { DateTimeOffset bad(DateTime(2026, 1, 1), TimeSpan::FromHours(15.0)); (void)bad; } catch (const std::runtime_error&) { threw = 1; }
    printf(""h15_throws=%d\n"", threw);
    threw = 0;
    try { DateTimeOffset bad(DateTime(2026, 1, 1), TimeSpan::FromTicks(5)); (void)bad; } catch (const std::runtime_error&) { threw = 1; }
    printf(""subminute_throws=%d\n"", threw);
    threw = 0;
    try {
        DateTimeOffset bad(DateTime::FromTicksAndKind(DateTime(2026, 1, 1).Ticks(), DateTime::KindUtc),
                           TimeSpan::FromHours(2.0));
        (void)bad;
    } catch (const std::runtime_error&) { threw = 1; }
    printf(""utc_nonzero_throws=%d\n"", threw);
    /* Utc-kind one-arg ctor -> offset 0, same instant */
    DateTimeOffset uk(DateTime::FromTicksAndKind(DateTime(2026, 1, 1, 8, 0, 0).Ticks(), DateTime::KindUtc));
    printf(""utc_ctor=%d\n"", uk == a && uk.Offset() == TimeSpan::Zero());
    /* ToOffset preserves the instant, shifts the clock (verified .NET: 13:00 +05:00) */
    DateTimeOffset t = a.ToOffset(TimeSpan::FromHours(5.0));
    printf(""tooffset_eq=%d\n"", t == a);
    printf(""tooffset_clock=%d\n"", t.ClockDateTime() == DateTime(2026, 1, 1, 13, 0, 0));
    printf(""tooffset_off=%d\n"", t.Offset() == TimeSpan::FromHours(5.0));
    printf(""touniv=%d\n"", a.ToUniversalTime().Offset() == TimeSpan::Zero() && a.ToUniversalTime() == a);
    /* ordering across offsets is instant-based */
    DateTimeOffset later(DateTime(2026, 1, 1, 11, 0, 0), TimeSpan::FromHours(2.0));
    printf(""lt=%d\n"", a < later && later > a && a.CompareTo(later) == -1);
    /* invariant ToString ""MM/dd/yyyy HH:mm:ss zzz"" (verified .NET, both signs) */
    printf(""str=%d\n"", a.ToString() == ""01/01/2026 10:00:00 +02:00"");
    DateTimeOffset neg(DateTime(2026, 1, 1, 10, 0, 0), TimeSpan::FromHours(-5.0));
    printf(""str_neg=%d\n"", neg.ToString() == ""01/01/2026 10:00:00 -05:00"");
    std::ostringstream oss;
    oss << a;
    printf(""stream=%d\n"", oss.str() == a.ToString());
    /* hash = UTC instant, equal across offsets — matches equality (spec 6.2) */
    printf(""hash=%d\n"", std::hash<DateTimeOffset>{}(a) == std::hash<DateTimeOffset>{}(b));
    /* OS local seam: Now/UtcNow same instant modulo the call gap; local round-trip */
    DateTimeOffset now = DateTimeOffset::Now(), un = DateTimeOffset::UtcNow();
    printf(""now_close=%d\n"", (un.UtcDateTime() - now.UtcDateTime()).Duration() < TimeSpan(0, 2, 0));
    printf(""utcnow_off0=%d\n"", un.Offset() == TimeSpan::Zero());
    printf(""now_off_range=%d\n"", now.Offset().Duration() <= TimeSpan(14, 0, 0));
    DateTime loc = u.LocalDateTime();
    printf(""local_kind=%d\n"", loc.Kind() == DateTime::KindLocal);
    DateTimeOffset back(loc);
    printf(""local_rt=%d\n"", back == u);
    printf(""tolocal_eq=%d\n"", u.ToLocalTime() == u);
    return 0;
}
");
        AssertMarkers(output,
            "instant_eq", "cmp0", "utcdt", "utcdt_kind", "clockdt", "clock_kind", "ticks_clock", "offset",
            "unix0", "unix0_off", "unix2026", "unix_rt", "unixms_rt", "unixms_from", "unix_neg",
            "h15_throws", "subminute_throws", "utc_nonzero_throws", "utc_ctor",
            "tooffset_eq", "tooffset_clock", "tooffset_off", "touniv", "lt",
            "str", "str_neg", "stream", "hash",
            "now_close", "utcnow_off0", "now_off_range", "local_kind", "local_rt", "tolocal_eq");
    }

    /// <summary>
    /// StringBuilder battery (spec §3/§9/§11): shared_ptr chaining (make_shared ALWAYS —
    /// enable_shared_from_this), typed Append overloads (int/bool per the plan), the ALIASING
    /// reference-semantics proof, byte-index Insert/Remove with range-checked throws, Replace-all,
    /// AppendFormat {0}, and UTF-8 BYTE Length for a 2-byte char (documented divergence from
    /// .NET's UTF-16 count of 1).
    /// </summary>
    [Test, Category("Integration")]
    public void StringBuilder_Chaining_Aliasing_ByteIndices_Throws()
    {
        var output = Run(@"
#include <sstream>
using namespace BasicLang;
static void appendVia(std::shared_ptr<StringBuilder> sb) { sb->Append(""!""); }
int main() {
    /* NB: always make_shared — enable_shared_from_this is UB on a bare object */
    auto sb = std::make_shared<StringBuilder>();
    auto r = sb->Append(""a"")->Append(""b"")->AppendLine(""c"");
    printf(""chain=%d\n"", sb->ToString() == ""abc\n"");
    printf(""chain_same=%d\n"", r.get() == sb.get());
    /* typed Appends (verified .NET: True427; bool must NOT print 1/0) */
    auto t = std::make_shared<StringBuilder>();
    t->Append(true)->Append((int32_t)42)->Append((int64_t)7);
    printf(""typed=%d\n"", t->ToString() == ""True427"");
    auto f = std::make_shared<StringBuilder>();
    f->Append(false);
    printf(""false=%d\n"", f->ToString() == ""False"");
    /* Append(double) matches the backend's std::to_string style */
    auto dbl = std::make_shared<StringBuilder>();
    dbl->Append(1.5);
    printf(""dbl=%d\n"", dbl->ToString() == std::to_string(1.5));
    /* ALIASING: two shared_ptr, ONE builder — reference semantics (spec 3) */
    auto p1 = std::make_shared<StringBuilder>(std::string(""x""));
    auto p2 = p1;
    p2->Append(""y"");
    appendVia(p1);
    printf(""alias=%d\n"", p1->ToString() == ""xy!"" && p2->ToString() == ""xy!"");
    /* byte-index Insert/Remove (verified .NET: h..ello / hello) */
    auto ins = std::make_shared<StringBuilder>(std::string(""hello""));
    ins->Insert(1, "".."");
    printf(""insert=%d\n"", ins->ToString() == ""h..ello"");
    ins->Remove(1, 2);
    printf(""remove=%d\n"", ins->ToString() == ""hello"");
    /* range checks throw std::runtime_error (spec 11) */
    int threw = 0;
    try { std::make_shared<StringBuilder>(std::string(""ab""))->Insert(3, ""x""); } catch (const std::runtime_error&) { threw = 1; }
    printf(""insert_oor=%d\n"", threw);
    threw = 0;
    try { std::make_shared<StringBuilder>(std::string(""ab""))->Insert(-1, ""x""); } catch (const std::runtime_error&) { threw = 1; }
    printf(""insert_neg=%d\n"", threw);
    threw = 0;
    try { std::make_shared<StringBuilder>(std::string(""ab""))->Remove(1, 5); } catch (const std::runtime_error&) { threw = 1; }
    printf(""remove_oor=%d\n"", threw);
    /* Replace hits every occurrence (verified .NET: aYYbYYc) */
    auto rep = std::make_shared<StringBuilder>(std::string(""aXbXc""));
    rep->Replace(""X"", ""YY"");
    printf(""replace=%d\n"", rep->ToString() == ""aYYbYYc"");
    /* AppendFormat {0}-only v1 (verified .NET: aZbZ) */
    auto fmt = std::make_shared<StringBuilder>();
    fmt->AppendFormat(""a{0}b{0}"", ""Z"");
    printf(""fmt=%d\n"", fmt->ToString() == ""aZbZ"");
    /* Length counts UTF-8 BYTES: 2 for a 2-byte char (spec 9 documented divergence; .NET says 1) */
    auto u8 = std::make_shared<StringBuilder>(std::string(""\xC3\xA9""));
    printf(""len_bytes=%d\n"", u8->Length() == 2);
    u8->Insert(0, ""a"");
    printf(""len_bytes3=%d\n"", u8->Length() == 3);
    auto c = std::make_shared<StringBuilder>(std::string(""abc""));
    c->Clear();
    printf(""clear=%d\n"", c->Length() == 0 && c->ToString() == """");
    printf(""cap=%d\n"", std::make_shared<StringBuilder>(std::string(""abc""))->Capacity() >= 3);
    /* shared_ptr stream inserter */
    std::ostringstream oss;
    oss << p1;
    printf(""stream=%d\n"", oss.str() == p1->ToString());
    return 0;
}
");
        AssertMarkers(output,
            "chain", "chain_same", "typed", "false", "dbl", "alias",
            "insert", "remove", "insert_oor", "insert_neg", "remove_oor",
            "replace", "fmt", "len_bytes", "len_bytes3", "clear", "cap", "stream");
    }

    /// <summary>
    /// Task 7 riders on the Task 6 code: (1) TimeSpan::Parse 8-digit day counts throw cleanly
    /// (previously UB int64 overflow) and MinValue round-trips through Parse (its magnitude is
    /// INT64_MAX+1 — one tick past what a positive accumulation can hold); (2) the Interval
    /// bound is INCLUSIVE like .NET's. All values verified against real .NET.
    /// </summary>
    [Test, Category("Integration")]
    public void Riders_ParseDayGuard_MinValueRoundTrip_IntervalInclusiveBound()
    {
        var output = Run(@"
using namespace BasicLang;
int main() {
    int threw = 0;
    try { TimeSpan::Parse(""99999999.00:00:00""); } catch (const std::runtime_error&) { threw = 1; }
    printf(""daymag_throws=%d\n"", threw);
    threw = 0;
    try { TimeSpan::Parse(""10675200.00:00:00""); } catch (const std::runtime_error&) { threw = 1; }
    printf(""dayedge_throws=%d\n"", threw);
    threw = 0;
    try { TimeSpan::Parse(""10675199.23:00:00""); } catch (const std::runtime_error&) { threw = 1; }
    printf(""inrange_days_overflow_throws=%d\n"", threw);
    printf(""min_str=%d\n"", TimeSpan::MinValue().ToString() == ""-10675199.02:48:05.4775808"");
    printf(""min_rt=%d\n"", TimeSpan::Parse(TimeSpan::MinValue().ToString()) == TimeSpan::MinValue());
    printf(""max_str=%d\n"", TimeSpan::MaxValue().ToString() == ""10675199.02:48:05.4775807"");
    printf(""max_rt=%d\n"", TimeSpan::Parse(TimeSpan::MaxValue().ToString()) == TimeSpan::MaxValue());
    /* inclusive Interval bound (verified .NET: accepted, ticks 9223372036854770000) */
    printf(""bound_pos=%d\n"", TimeSpan::FromMilliseconds(922337203685476.5).Ticks() == 9223372036854770000LL);
    printf(""bound_neg=%d\n"", TimeSpan::FromMilliseconds(-922337203685476.5).Ticks() == -9223372036854770000LL);
    threw = 0;
    try { TimeSpan::FromMilliseconds(922337203685477.0); } catch (const std::runtime_error&) { threw = 1; }
    printf(""over_throws=%d\n"", threw);
    return 0;
}
");
        AssertMarkers(output,
            "daymag_throws", "dayedge_throws", "inrange_days_overflow_throws",
            "min_str", "min_rt", "max_str", "max_rt",
            "bound_pos", "bound_neg", "over_throws");
    }
}

/// <summary>
/// Tests for the faithful 96-bit Decimal engine <c>bl_decimal.hpp</c>
/// (<see cref="CppDecimalRuntime"/>, spec 2026-07-27-p1-native-bcl-types §10/§11/§12,
/// plan Task 8). Sibling fixture to <see cref="CppBclRuntimeTests"/> in the same file;
/// <c>bl_decimal.hpp</c> is INDEPENDENT of <c>bl_bcltypes.hpp</c> (Task 9 splices both).
///
/// FAST section: content pins (banner, struct, hash, ostream inserter, include-free Body,
/// runtime_error-only throw discipline — spec §11: logic_error-derived types would slip
/// past the backend's <c>catch (std::runtime_error)</c> lowering).
///
/// INTEGRATION section: the plan's LOCKED vector battery, one native program per vector
/// group, marker-style (<c>name=1</c>). EVERY expected value verified against real .NET
/// (PowerShell <c>[decimal]</c> oracle, Parse-based expressions — PS 5.1 constant-folds
/// literal-only decimal arithmetic through a normalizing path, so literal readings lie).
/// Oracle-driven vector corrections vs the plan draft: none — all plan vectors held; the
/// battery ADDS oracle-pinned cases the plan left open (exact division keeps scale
/// <c>sa-sb</c>: 10.00/4 = "2.50"; an INEXACT quotient rounding to zero is plain "0"
/// while exact zero keeps scale: 0.0/3 = "0.0"; Decimal(double) is .NET's
/// 15-significant-digit rule, NOT shortest-round-trip — (decimal)(1.0/3.0) has 15 threes).
/// </summary>
[TestFixture]
public class CppDecimalRuntimeTests
{
    // ---------------- FAST: content pins (spec §12 layer 1) ----------------

    [Test]
    public void DecimalHeader_ContainsSourceOfTruthBanner() =>
        Assert.That(CppDecimalRuntime.DecimalHeader,
            Does.Contain("SOURCE OF TRUTH: BasicLang CppDecimalRuntime.cs"));

    [Test]
    public void DecimalHeader_DefinesDecimalStruct() =>
        Assert.That(CppDecimalRuntime.DecimalHeader, Does.Contain("struct Decimal"));

    [Test]
    public void DecimalHeader_DefinesHashSpecialization() =>
        Assert.That(CppDecimalRuntime.DecimalHeader, Does.Contain("std::hash<BasicLang::Decimal>"));

    [Test]
    public void DecimalHeader_DefinesOstreamInserter() =>
        Assert.That(CppDecimalRuntime.DecimalHeader,
            Does.Contain("operator<<(std::ostream& os, const Decimal& v)"));

    /// <summary>
    /// Spec §11: every P1 runtime throw must be <c>std::runtime_error</c> — never a
    /// logic_error-derived type, which the backend's Try/Catch lowering would miss.
    /// </summary>
    [Test]
    public void DecimalHeader_ThrowsOnlyRuntimeError()
    {
        Assert.That(CppDecimalRuntime.DecimalHeader, Does.Contain("std::runtime_error"));
        Assert.That(CppDecimalRuntime.DecimalHeader, Does.Not.Contain("std::logic_error"));
        Assert.That(CppDecimalRuntime.DecimalHeader, Does.Not.Contain("std::invalid_argument"));
        Assert.That(CppDecimalRuntime.DecimalHeader, Does.Not.Contain("std::out_of_range"));
    }

    /// <summary>bl_decimal.hpp stands alone — Task 9 splices it NEXT TO bl_bcltypes.hpp, not through it.</summary>
    [Test]
    public void DecimalHeader_DoesNotIncludeBclTypesHeader() =>
        Assert.That(CppDecimalRuntime.DecimalHeader, Does.Not.Contain("bl_bcltypes"));

    /// <summary>
    /// Task 9 splice preconditions, mirroring <c>BclBody_IsIncludeFreeAndOpensNamespace</c>:
    /// include-free Body opening its own namespace; whole-file == Includes + Body.
    /// </summary>
    [Test]
    public void DecimalBody_IsIncludeFreeAndOpensNamespace()
    {
        Assert.That(CppDecimalRuntime.DecimalBody, Does.Not.Contain("#include"));
        Assert.That(CppDecimalRuntime.DecimalBody, Does.Not.Contain("#pragma once"));
        Assert.That(CppDecimalRuntime.DecimalBody.TrimStart(), Does.StartWith("namespace BasicLang {"));
        Assert.That(CppDecimalRuntime.DecimalHeader,
            Is.EqualTo(CppDecimalRuntime.DecimalIncludes + CppDecimalRuntime.DecimalBody));
    }

    // ---------------- INTEGRATION: the locked vector battery ----------------

    private (string exe, string argsTemplate)? _compiler;

    [OneTimeSetUp]
    public void FindCompiler() => _compiler = Native.CppCompile.FindRunCompiler();

    /// <summary>
    /// Shared program prelude: parse/format/throw helpers so each vector group reads as
    /// one line per oracle-verified case.
    /// </summary>
    private const string Prelude = @"
#include <functional>
#include <sstream>
using namespace BasicLang;
static bool thr(const std::function<void()>& f) { try { f(); } catch (const std::runtime_error&) { return true; } return false; }
static std::string S(const Decimal& d) { return d.ToString(); }
static Decimal P(const char* s) { return Decimal::Parse(s); }
";

    private string Run(string mainBody)
    {
        if (_compiler is null) Assert.Ignore("No C++ compiler available");
        var src = "#include \"bl_decimal.hpp\"\n#include <cstdio>\n" + Prelude + mainBody;
        return Native.CppCompile.CompileAndRun(src, _compiler!.Value, new Dictionary<string, string>
        {
            ["bl_decimal.hpp"] = CppDecimalRuntime.DecimalHeader,
        });
    }

    private static void AssertMarkers(string output, params string[] markers)
    {
        foreach (var m in markers)
            Assert.That(output, Does.Contain(m + "=1"), $"marker '{m}' not 1; full output:\n{output}");
    }

    [Test, Category("Integration")]
    public void HeaderCompilesStandalone() =>
        Assert.That(Run("int main(){ printf(\"ok\"); return 0; }"), Does.Contain("ok"));

    /// <summary>
    /// Construction, scale-preserving ToString, Parse round-trips incl. 28-frac-digit values
    /// and MaxValue, >28-digit round-half-even (ties both ways), overflow/format throws.
    /// </summary>
    [Test, Category("Integration")]
    public void Construction_ToString_Parse_RoundTrips()
    {
        var output = Run(@"
int main() {
    printf(""fromparts_150=%d\n"", S(Decimal::FromParts(150, 0, 0, false, 2)) == ""1.50"");
    printf(""int_5=%d\n"", S(Decimal(5)) == ""5"");
    printf(""max_str=%d\n"", S(Decimal::MaxValue()) == ""79228162514264337593543950335"");
    printf(""min_str=%d\n"", S(Decimal::MinValue()) == ""-79228162514264337593543950335"");
    printf(""zero_one=%d\n"", S(Decimal::Zero()) == ""0"" && S(Decimal::One()) == ""1"");
    printf(""neg_int=%d\n"", S(Decimal(-42)) == ""-42"");
    printf(""i64=%d\n"", S(Decimal((int64_t)-9223372036854775807LL)) == ""-9223372036854775807"");
    printf(""parse_150=%d\n"", P(""1.50"") == Decimal::FromParts(150, 0, 0, false, 2) && S(P(""1.50"")) == ""1.50"");
    printf(""parse_forms=%d\n"", S(P("" 1.5 "")) == ""1.5"" && S(P(""+1.5"")) == ""1.5"" && S(P(""-0.5"")) == ""-0.5"" && S(P(""1."")) == ""1"" && S(P("".5"")) == ""0.5"");
    printf(""parse_28frac=%d\n"", S(P(""0.0000000000000000000000000001"")) == ""0.0000000000000000000000000001"");
    printf(""parse_max_rt=%d\n"", S(P(""79228162514264337593543950335"")) == ""79228162514264337593543950335"");
    printf(""parse_29frac=%d\n"", S(P(""0.12345678901234567890123456789"")) == ""0.1234567890123456789012345679"");
    printf(""parse_tie_even=%d\n"", S(P(""0.000000000000000000000000000250"")) == ""0.0000000000000000000000000002"");
    printf(""parse_tie_odd=%d\n"", S(P(""0.000000000000000000000000000150"")) == ""0.0000000000000000000000000002"");
    printf(""parse_intfrac=%d\n"", S(P(""12345678901234567890123456789.123"")) == ""12345678901234567890123456789"");
    printf(""parse_over_throws=%d\n"", thr([] { P(""99999999999999999999999999999""); }));
    printf(""parse_bad_throws=%d\n"", thr([] { P(""abc""); }) && thr([] { P(""""); }) && thr([] { P(""1.2.3""); }) && thr([] { P("".""); }) && thr([] { P(""-""); }));
    printf(""rt_pi=%d\n"", S(P(""3.14159265358979323846264338"")) == ""3.14159265358979323846264338"");
    return 0;
}
");
        AssertMarkers(output,
            "fromparts_150", "int_5", "max_str", "min_str", "zero_one", "neg_int", "i64",
            "parse_150", "parse_forms", "parse_28frac", "parse_max_rt", "parse_29frac",
            "parse_tie_even", "parse_tie_odd", "parse_intfrac",
            "parse_over_throws", "parse_bad_throws", "rt_pi");
    }

    /// <summary>
    /// Add/sub: max-scale rule (1.0+1.00 = "2.00"), 0.1+0.2 == 0.3 EXACTLY, the
    /// thousand-iteration 0.10 accumulation == "100.00", overflow reduction with a single
    /// round-half-even decision (Max+0.45 == Max, Max+0.5 tie rounds up on the odd last
    /// digit and throws), unary minus, ++/-- preserving scale (1.50 -> 2.50).
    /// </summary>
    [Test, Category("Integration")]
    public void AddSub_ScaleRules_MoneyLoop_UnaryIncrement()
    {
        var output = Run(@"
int main() {
    printf(""add_335=%d\n"", S(P(""1.1"") + P(""2.25"")) == ""3.35"" && (P(""1.1"") + P(""2.25"")) == P(""3.35""));
    printf(""add_point3=%d\n"", (P(""0.1"") + P(""0.2"")) == P(""0.3""));
    printf(""add_200=%d\n"", S(P(""1.0"") + P(""1.00"")) == ""2.00"");
    printf(""add_250=%d\n"", S(P(""1.50"") + Decimal(1)) == ""2.50"");
    printf(""sub_zero_scale=%d\n"", S(P(""1.5"") - P(""1.5"")) == ""0.0"" && S(P(""1.50"") - P(""1.50"")) == ""0.00"");
    printf(""max_p045=%d\n"", (Decimal::MaxValue() + P(""0.45"")) == Decimal::MaxValue());
    printf(""max_p05_throws=%d\n"", thr([] { Decimal x = Decimal::MaxValue() + P(""0.5""); (void)x; }));
    printf(""max_p06_throws=%d\n"", thr([] { Decimal x = Decimal::MaxValue() + P(""0.6""); (void)x; }));
    printf(""max_p1_throws=%d\n"", thr([] { Decimal x = Decimal::MaxValue() + Decimal(1); (void)x; }));
    printf(""min_m1_throws=%d\n"", thr([] { Decimal x = Decimal::MinValue() - Decimal(1); (void)x; }));
    Decimal sum;
    Decimal dime = P(""0.10"");
    for (int i = 0; i < 1000; ++i) sum += dime;
    printf(""money_loop=%d\n"", sum == Decimal(100) && S(sum) == ""100.00"");
    printf(""unary_neg=%d\n"", S(-P(""1.5"")) == ""-1.5"");
    printf(""unary_neg_zero=%d\n"", S(-P(""0.00"")) == ""0.00"");
    Decimal d = P(""1.50"");
    ++d;
    printf(""preinc=%d\n"", S(d) == ""2.50"");
    Decimal old = d++;
    printf(""postinc=%d\n"", S(old) == ""2.50"" && S(d) == ""3.50"");
    --d;
    printf(""predec=%d\n"", S(d) == ""2.50"");
    old = d--;
    printf(""postdec=%d\n"", S(old) == ""2.50"" && S(d) == ""1.50"");
    return 0;
}
");
        AssertMarkers(output,
            "add_335", "add_point3", "add_200", "add_250", "sub_zero_scale",
            "max_p045", "max_p05_throws", "max_p06_throws", "max_p1_throws", "min_m1_throws",
            "money_loop", "unary_neg", "unary_neg_zero",
            "preinc", "postinc", "predec", "postdec");
    }

    /// <summary>
    /// Mul: scale = s1+s2 (12.0*10.0 = "120.00", 12.0*10 = "120.0", 19.99*100 = "1999.00"),
    /// 192-bit product reduction with round-half-even, zero keeps min(s1+s2, 28), overflow.
    /// </summary>
    [Test, Category("Integration")]
    public void Multiplication_ScaleAndRounding_Overflow()
    {
        var output = Run(@"
int main() {
    printf(""mul_12000=%d\n"", S(P(""12.0"") * P(""10.0"")) == ""120.00"");
    printf(""mul_1200=%d\n"", S(P(""12.0"") * Decimal(10)) == ""120.0"");
    printf(""mul_1999=%d\n"", S(P(""19.99"") * Decimal(100)) == ""1999.00"");
    printf(""mul_tiny=%d\n"", S(P(""0.5000000000000000000000000001"") * P(""0.5000000000000000000000000001"")) == ""0.2500000000000000000000000001"");
    printf(""mul_zero_scale=%d\n"", S(P(""1.5"") * Decimal(0)) == ""0.0"");
    printf(""mul_zero_scale5=%d\n"", S(P(""0.00"") * P(""0.000"")) == ""0.00000"");
    printf(""mul_max10_throws=%d\n"", thr([] { Decimal x = Decimal::MaxValue() * Decimal(10); (void)x; }));
    printf(""mul_1e19sq_throws=%d\n"", thr([] { Decimal x = P(""10000000000000000000"") * P(""10000000000000000000""); (void)x; }));
    printf(""mul_neg=%d\n"", S(P(""-1.5"") * Decimal(2)) == ""-3.0"");
    return 0;
}
");
        AssertMarkers(output,
            "mul_12000", "mul_1200", "mul_1999", "mul_tiny",
            "mul_zero_scale", "mul_zero_scale5",
            "mul_max10_throws", "mul_1e19sq_throws", "mul_neg");
    }

    /// <summary>
    /// Div: 1/3 is 28 threes, 2/3 rounds the last digit up, EXACT division keeps scale
    /// sa-sb (10/4 = "2.5" but 10.00/4 = "2.50"), negative initial scale pre-scales the
    /// dividend (2/0.5 = "4"), 29-significant-digit capacity (8/0.9), an inexact quotient
    /// rounding to zero is plain "0" (1/2e28 — .NET bits {0,0,0,0}) while an exact zero
    /// keeps its scale (0.0/3 = "0.0"), divide-by-zero and quotient overflow throw.
    /// </summary>
    [Test, Category("Integration")]
    public void Division_DigitExtension_Rounding_Throws()
    {
        var output = Run(@"
int main() {
    printf(""div_third=%d\n"", S(Decimal(1) / Decimal(3)) == ""0.3333333333333333333333333333"");
    printf(""div_two_thirds=%d\n"", S(Decimal(2) / Decimal(3)) == ""0.6666666666666666666666666667"");
    printf(""div_25=%d\n"", S(Decimal(10) / Decimal(4)) == ""2.5"");
    printf(""div_250=%d\n"", S(P(""10.00"") / Decimal(4)) == ""2.50"");
    printf(""div_12=%d\n"", S(P(""12.0"") / Decimal(10)) == ""1.2"");
    printf(""div_eighth=%d\n"", S(Decimal(1) / Decimal(8)) == ""0.125"");
    printf(""div_4=%d\n"", S(Decimal(2) / P(""0.5"")) == ""4"");
    printf(""div_8=%d\n"", S(Decimal(1) / P(""0.125"")) == ""8"");
    printf(""div_20=%d\n"", S(Decimal(10) / P(""0.5"")) == ""20"");
    printf(""div_keep=%d\n"", S(P(""3.0"") / Decimal(3)) == ""1.0"" && S(P(""1.00"") / Decimal(1)) == ""1.00"" && S(Decimal(100) / Decimal(10)) == ""10"");
    printf(""div_zero_q=%d\n"", S(Decimal(1) / P(""20000000000000000000000000000"")) == ""0"");
    printf(""div_one_ulp=%d\n"", S(P(""1.5"") / P(""20000000000000000000000000000"")) == ""0.0000000000000000000000000001"");
    printf(""div_zero_exact=%d\n"", S(P(""0.0"") / Decimal(3)) == ""0.0"");
    printf(""div_889=%d\n"", S(Decimal(8) / P(""0.9"")) == ""8.888888888888888888888888889"");
    printf(""div_778=%d\n"", S(Decimal(7) / P(""0.9"")) == ""7.7777777777777777777777777778"");
    printf(""div_byzero_throws=%d\n"", thr([] { Decimal x = Decimal(1) / Decimal(0); (void)x; }));
    printf(""div_over_throws=%d\n"", thr([] { Decimal x = Decimal::MaxValue() / P(""0.1""); (void)x; }));
    printf(""div_negsign=%d\n"", S(Decimal(-1) / Decimal(4)) == ""-0.25"");
    return 0;
}
");
        AssertMarkers(output,
            "div_third", "div_two_thirds", "div_25", "div_250", "div_12", "div_eighth",
            "div_4", "div_8", "div_20", "div_keep", "div_zero_q", "div_one_ulp",
            "div_zero_exact", "div_889", "div_778",
            "div_byzero_throws", "div_over_throws", "div_negsign");
    }

    /// <summary>
    /// Mod: dividend sign (3.5 Mod 1 = 0.5, -3.5 Mod 1 = -0.5), max-scale rule
    /// (3.50 Mod 1 = "0.50", 7.26 Mod 2.1 = "0.96"), exact remainder, by-zero throws.
    /// </summary>
    [Test, Category("Integration")]
    public void Mod_DividendSign_MaxScale()
    {
        var output = Run(@"
int main() {
    printf(""mod_05=%d\n"", S(P(""3.5"") % Decimal(1)) == ""0.5"");
    printf(""mod_neg05=%d\n"", S(P(""-3.5"") % Decimal(1)) == ""-0.5"");
    printf(""mod_050=%d\n"", S(P(""3.50"") % Decimal(1)) == ""0.50"");
    printf(""mod_ints=%d\n"", S(Decimal(7) % Decimal(3)) == ""1"" && S(Decimal(-7) % Decimal(3)) == ""-1"");
    printf(""mod_096=%d\n"", S(P(""7.26"") % P(""2.1"")) == ""0.96"");
    printf(""mod_zero_scale=%d\n"", S(P(""2.0"") % Decimal(1)) == ""0.0"");
    printf(""mod_byzero_throws=%d\n"", thr([] { Decimal x = Decimal(1) % Decimal(0); (void)x; }));
    return 0;
}
");
        AssertMarkers(output,
            "mod_05", "mod_neg05", "mod_050", "mod_ints", "mod_096",
            "mod_zero_scale", "mod_byzero_throws");
    }

    /// <summary>
    /// Value equality across scales (1.0 == 1.00), static Compare returning -1/0/1,
    /// CompareTo, full relational set, and the scale-NORMALIZED std::hash (1.0 and 1.00
    /// hash equal; zero is canonical for any scale).
    /// </summary>
    [Test, Category("Integration")]
    public void Equality_Compare_Hash_AcrossScales()
    {
        var output = Run(@"
int main() {
    printf(""eq_scales=%d\n"", P(""1.0"") == P(""1.00""));
    printf(""cmp_scales=%d\n"", Decimal::Compare(P(""1.0""), P(""1.00"")) == 0);
    printf(""cmp_12=%d\n"", Decimal::Compare(Decimal(1), Decimal(2)) == -1);
    printf(""cmp_21=%d\n"", Decimal::Compare(Decimal(2), Decimal(1)) == 1);
    printf(""cmpto=%d\n"", P(""1.0"").CompareTo(P(""1.00"")) == 0 && Decimal(1).CompareTo(Decimal(2)) == -1);
    printf(""cmp_151=%d\n"", Decimal::Compare(P(""1.5""), P(""1.51"")) == -1);
    printf(""order_neg=%d\n"", Decimal(-2) < Decimal(-1) && Decimal(-1) < Decimal(0) && Decimal(0) < Decimal(1));
    printf(""relops=%d\n"", P(""1.5"") <= P(""1.50"") && P(""1.5"") >= P(""1.50"") && P(""1.4"") < P(""1.5"") && P(""1.6"") > P(""1.5"") && P(""1.5"") != P(""1.6""));
    std::hash<Decimal> h;
    printf(""hash_scales=%d\n"", h(P(""1.0"")) == h(P(""1.00"")));
    printf(""hash_zero=%d\n"", h(P(""0.00"")) == h(Decimal(0)));
    printf(""hash_diff=%d\n"", h(P(""1.5"")) != h(P(""1.51"")));
    printf(""hash_sign=%d\n"", h(P(""1.5"")) != h(P(""-1.5"")));
    return 0;
}
");
        AssertMarkers(output,
            "eq_scales", "cmp_scales", "cmp_12", "cmp_21", "cmpto", "cmp_151",
            "order_neg", "relops",
            "hash_scales", "hash_zero", "hash_diff", "hash_sign");
    }

    /// <summary>
    /// Banker's Round (2.5 -> 2, 3.5 -> 4, 2.675@2 -> "2.68" — exact decimal, unlike
    /// double), Round result drops to the requested scale only, Truncate/Floor/Ceiling
    /// per .NET on negatives.
    /// </summary>
    [Test, Category("Integration")]
    public void Round_Truncate_Floor_Ceiling_Bankers()
    {
        var output = Run(@"
int main() {
    printf(""round_25=%d\n"", S(Decimal::Round(P(""2.5""))) == ""2"");
    printf(""round_35=%d\n"", S(Decimal::Round(P(""3.5""))) == ""4"");
    printf(""round_neg25=%d\n"", S(Decimal::Round(P(""-2.5""))) == ""-2"");
    printf(""round_2675=%d\n"", S(Decimal::Round(P(""2.675""), 2)) == ""2.68"");
    printf(""round_neg2675=%d\n"", S(Decimal::Round(P(""-2.675""), 2)) == ""-2.68"");
    printf(""round_25_1=%d\n"", S(Decimal::Round(P(""2.5""), 1)) == ""2.5"");
    printf(""round_1100_2=%d\n"", S(Decimal::Round(P(""1.100""), 2)) == ""1.10"");
    printf(""round_digits_throws=%d\n"", thr([] { Decimal x = Decimal::Round(P(""1.5""), 29); (void)x; }));
    printf(""trunc_neg37=%d\n"", S(Decimal::Truncate(P(""-3.7""))) == ""-3"");
    printf(""trunc_300=%d\n"", S(Decimal::Truncate(P(""3.00""))) == ""3"");
    printf(""floor_neg37=%d\n"", S(Decimal::Floor(P(""-3.7""))) == ""-4"");
    printf(""floor_37=%d\n"", S(Decimal::Floor(P(""3.7""))) == ""3"");
    printf(""ceil_neg37=%d\n"", S(Decimal::Ceiling(P(""-3.7""))) == ""-3"");
    printf(""ceil_37=%d\n"", S(Decimal::Ceiling(P(""3.7""))) == ""4"");
    return 0;
}
");
        AssertMarkers(output,
            "round_25", "round_35", "round_neg25", "round_2675", "round_neg2675",
            "round_25_1", "round_1100_2", "round_digits_throws",
            "trunc_neg37", "trunc_300", "floor_neg37", "floor_37", "ceil_neg37", "ceil_37");
    }

    /// <summary>
    /// Decimal(double) (.NET's 15-significant-digit VarDecFromR8 rule: (decimal)0.1 == 0.1m,
    /// (decimal)(1.0/3.0) = 15 threes, trailing zeros stripped) and ToDouble (.NET's exact
    /// VarR8FromDec formula — pinned by ToDouble(1e-28m) == 1.0/1e28 in double space, which
    /// is NOT the same double as the 1e-28 literal). Plus the ostream inserter.
    /// </summary>
    [Test, Category("Integration")]
    public void DoubleConversions_BothDirections()
    {
        var output = Run(@"
int main() {
    printf(""dbl_15=%d\n"", S(Decimal(1.5)) == ""1.5"" && Decimal(1.5).ToDouble() == 1.5);
    printf(""dbl_01=%d\n"", Decimal(0.1) == P(""0.1"") && S(Decimal(0.1)) == ""0.1"");
    printf(""dbl_01_rt=%d\n"", Decimal(P(""0.1"").ToDouble()) == P(""0.1""));
    printf(""dbl_third=%d\n"", S(Decimal(1.0 / 3.0)) == ""0.333333333333333"");
    printf(""dbl_15sig=%d\n"", S(Decimal(123456789.123456789)) == ""123456789.123457"");
    printf(""dbl_1em20=%d\n"", S(Decimal(1e-20)) == ""0.00000000000000000001"");
    printf(""dbl_1e28=%d\n"", S(Decimal(1e28)) == ""10000000000000000000000000000"");
    printf(""dbl_big=%d\n"", S(Decimal(12345678901234567890.0)) == ""12345678901234600000"");
    printf(""dbl_8e28_throws=%d\n"", thr([] { Decimal x(8e28); (void)x; }));
    printf(""dbl_nan_throws=%d\n"", thr([] { double z = 0.0; Decimal x(z / z); (void)x; }));
    printf(""dbl_negzero=%d\n"", S(Decimal(-0.0)) == ""0"");
    printf(""todbl_max=%d\n"", Decimal::MaxValue().ToDouble() == 79228162514264337593543950336.0);
    printf(""todbl_1em28=%d\n"", P(""0.0000000000000000000000000001"").ToDouble() == 1.0 / 1e28);
    printf(""todbl_neg25=%d\n"", P(""-2.5"").ToDouble() == -2.5);
    std::ostringstream oss;
    oss << P(""-1.50"");
    printf(""ostream=%d\n"", oss.str() == ""-1.50"");
    return 0;
}
");
        AssertMarkers(output,
            "dbl_15", "dbl_01", "dbl_01_rt", "dbl_third", "dbl_15sig", "dbl_1em20",
            "dbl_1e28", "dbl_big", "dbl_8e28_throws", "dbl_nan_throws", "dbl_negzero",
            "todbl_max", "todbl_1em28", "todbl_neg25", "ostream");
    }
}
