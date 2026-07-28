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
    public void BclHeader_DefinesHashSpecializations()
    {
        Assert.That(CppBclRuntime.BclHeader, Does.Contain("std::hash<BasicLang::DateTime>"));
        Assert.That(CppBclRuntime.BclHeader, Does.Contain("std::hash<BasicLang::TimeSpan>"));
    }

    [Test]
    public void BclHeader_DefinesOstreamInserters()
    {
        Assert.That(CppBclRuntime.BclHeader,
            Does.Contain("operator<<(std::ostream& os, const TimeSpan& v)"));
        Assert.That(CppBclRuntime.BclHeader,
            Does.Contain("operator<<(std::ostream& os, const DateTime& v)"));
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
}
