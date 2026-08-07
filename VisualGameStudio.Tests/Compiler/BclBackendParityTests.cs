using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// P1 Task 13 / spec §12 layer 5 — THE PARITY ORACLE.
///
/// Every other layer verifies the native runtime against expectations a human wrote
/// down. This one does not: the same BasicLang program is compiled and run through
/// BOTH backends — the C# backend on real .NET, and the C++ backend natively — and
/// their stdout must be BYTE-IDENTICAL. Real .NET is the oracle, so a diff here is a
/// real semantic divergence in the native runtime, not a stale expectation. There is
/// deliberately no <c>Expected</c> column in the table below.
///
/// ── CULTURE FORCING (load-bearing, not belt-and-braces) ───────────────────────────
/// The C# backend emits a BARE <c>value.ToString(format)</c> with no
/// <c>InvariantCulture</c> argument, and DateTime's parameterless <c>ToString()</c> is
/// culture-sensitive by definition. The C++ side is pinned to INVARIANT culture (spec
/// §9). In a .NET custom format string ':' is the *time separator specifier* — replaced
/// by the culture's TimeSeparator — not a literal, so even "yyyy-MM-dd HH:mm:ss" is
/// culture-dependent on the C# leg. Without forcing, these tests would pass on an en-US
/// box and fail on a de-DE one, and the default-<c>ToString()</c> programs would fail
/// EVERYWHERE (en-US gives "7/28/2026 1:45:30 PM"; invariant gives "07/28/2026 13:45:30").
///
/// MECHANISM: the C#-produced executable is launched with
/// <c>DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1</c> in its environment (see
/// <see cref="InvariantCultureEnv"/>). It applies to the RUN ONLY — never to the build,
/// and never to production emission, which is unchanged. It was chosen over injecting a
/// <c>CultureInfo.DefaultThreadCurrentCulture</c> preamble into Main because the backend
/// has no Main-preamble hook and wrapping the generated entry point would mean the parity
/// programs no longer run through the shipped <c>BasicLang.exe build</c> path.
///
/// VERIFIED before adoption, on this en-US machine, with a throwaway .NET 8 console app
/// run twice (same binary, only the env var differing):
///   without → CurrentCulture 'en-US' | "7/28/2026 1:45:30 PM"  | default DateTime "1/1/0001 12:00:00 AM"
///   with    → CurrentCulture ''      | "07/28/2026 13:45:30"   | default DateTime "01/01/0001 00:00:00"
/// The second row is exactly what the C++ invariant "G" inserter prints. The mechanism is
/// therefore also self-checking IN THIS FIXTURE: <c>DefaultValues</c>,
/// <c>DateTimeArithmetic</c> and <c>DateTimeOffset</c> print default/parameterless
/// <c>ToString()</c> forms, so if the env var ever stops taking effect those cases fail
/// immediately with a visible AM/PM diff rather than silently degrading.
///
/// ── MANDATORY PROGRAM CONSTRAINTS (each is a guaranteed diff or compile break) ─────
/// All are PRE-EXISTING backend behaviour, none introduced by P1; they were paid for the
/// hard way in Tasks 10–12 and are restated here because this fixture does not inherit
/// <see cref="CppBclEndToEndTests"/>'s header.
///  1. Never print a raw Boolean — C++ prints 1/0, .NET prints True/False. Branch through
///     an <c>If</c> and print a word.
///  2. Never name a local <c>t0</c>/<c>t1</c>/any <c>t&lt;N&gt;</c> — collides with the
///     generator's temp names and emits a C++ redefinition.
///  3. Narrow with <c>CType(x, Integer)</c>, never <c>CInt(x)</c> — CInt truncates on C++
///     and emits <c>Convert.ToInt32</c> (rounds) on C#.
///  4. Use <c>.ToString()</c>, never <c>CStr(nativeValue)</c> — Task 10's conversion gate
///     correctly rejects the intrinsic form for the five non-Decimal native types.
///  5. Decimal→integral operands stay ≤15 significant digits AND in range — narrowing goes
///     through <c>ToDouble()</c> (the .NET VarR8FromDec rule) and out-of-range is UB on C++
///     where .NET throws.
///  6. No module-qualified <c>Sub</c> calls (<c>Helpers.Foo()</c>) — broken on the C++
///     backend generally. One <c>Module M</c> with local helpers only.
///  7. Never print <c>sb.Length</c> after an <c>AppendLine</c> — AppendLine appends '\n' on
///     C++ and <c>Environment.NewLine</c> ("\r\n") on .NET (spec §9). The CONTENT difference
///     normalizes away in this harness, the BYTE COUNT does not.
///  8. Nothing nondeterministic: no <c>Now</c>/<c>Today</c>/<c>NewGuid</c>/<c>DateTime.Now</c>
///     anywhere. The two legs are two separate runs, so a wall clock would diff against
///     ITSELF. Fixed literal dates and guids only (those APIs are covered structurally by
///     <see cref="CppBclEndToEndTests"/>).
///  9. No culture-sensitive string operations and no LOCAL-TIME conversions. The culture
///     forcing above is not a scalpel: <c>DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1</c>
///     disables ICU WHOLESALE, which also makes string comparison/sorting ORDINAL, makes
///     <c>ToUpper</c>/<c>ToLower</c> ASCII-only, and collapses <c>TimeZoneInfo</c> to a
///     single UTC zone. Those areas are therefore NOT stock .NET on the C# leg, so a
///     program exercising them would be comparing the native runtime against a crippled
///     oracle. Keep to formatting/parsing (which invariant culture pins exactly) and to
///     explicit-offset time (<c>DateTimeOffset</c> with a literal <c>TimeSpan</c>).
/// 10. ASCII-only output. Neither leg pins <c>StandardOutputEncoding</c>, and the two
///     decode stdout through different paths: the native program writes raw UTF-8 bytes
///     while .NET's Console encodes through the console/ANSI code page. Non-ASCII output
///     would therefore diff on ENCODING alone — a false failure that says nothing about
///     the runtime. (This is also why <c>StringBuilder</c>'s byte-vs-UTF-16 <c>Length</c>
///     divergence in spec §9 is not exercised here: ASCII makes the two counts equal.)
///
/// ── WHAT THIS ORACLE CANNOT SEE (read before trusting a green run) ────────────────
/// A differential oracle only catches bugs that behave DIFFERENTLY on the two backends.
/// It is structurally blind to a bug that behaves IDENTICALLY on both — and "identically
/// wrong" is a real shape here, not a hypothetical: a statement the FRONT END silently
/// drops (chip task_810dc83e — a bare <c>n++</c> statement is parsed and discarded)
/// disappears from BOTH emissions, so both legs print the same wrong thing and this
/// fixture passes. Green here means "the native runtime agrees with real .NET", never
/// "the compiler is semantically correct". Bugs of that shape need the hand-written
/// expectations in <see cref="CppBclEndToEndTests"/> and the native vector fixtures.
///
/// NonParallelizable and Integration: every case runs TWO full toolchains (a
/// <c>BasicLang.exe build</c> + <c>dotnet build</c> + run, and a BL→C++ compile + native
/// compile + run), each of which already saturates a core.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("Integration")]
public class BclBackendParityTests
{
    /// <summary>
    /// The one environment override applied to the C#-compiled executable. See the
    /// fixture header — this is the culture-forcing mechanism, and it is load-bearing.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> InvariantCultureEnv =
        new Dictionary<string, string> { ["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1" };

    /// <summary>
    /// Probe for the C++ compiler ONCE, before any case runs. The C# leg runs FIRST inside
    /// <see cref="RunBothBackends"/>, so without this every case would pay a full
    /// <c>BasicLang.exe build</c> + <c>dotnet build</c> + run (minutes across the table)
    /// only to hit <see cref="BclE2E.CompileRun"/>'s ignore on a machine that was never
    /// going to run the C++ leg. Same probe the C++ leg itself uses, so the two cannot
    /// disagree about availability.
    /// </summary>
    [OneTimeSetUp]
    public void RequireCppCompiler()
    {
        if (VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler() is null)
            Assert.Ignore("No C++ compiler available on this machine — the parity oracle needs both legs.");
    }

    /// <summary>
    /// Compile and run <paramref name="blSource"/> through both backends and return the two
    /// stdouts, line endings normalized to '\n'.
    ///
    /// C# leg: <see cref="CliTestHarness.CompileRunCSharp"/> — the shipped
    /// <c>BasicLang.exe build App.blproj</c> with <c>&lt;TargetBackend&gt;CSharp&lt;/TargetBackend&gt;</c>,
    /// then the produced exe under forced invariant culture.
    /// C++ leg: <see cref="BclE2E.CompileToCppOptimized"/> + a real C++ compiler — the same
    /// optimizer-running path <see cref="CppBclEndToEndTests"/>'s leg 1 uses (that fixture
    /// separately covers the <c>--target=cpp</c> CLI leg for the same language surface, so
    /// paying for a third toolchain per case here would buy nothing).
    /// </summary>
    private static (string CSharp, string Cpp) RunBothBackends(string blSource)
    {
        var csOut = CliTestHarness.CompileRunCSharp(blSource, InvariantCultureEnv);
        var cppOut = BclE2E.CompileRun(BclE2E.CompileToCppOptimized(blSource));
        return (csOut.Replace("\r\n", "\n"), cppOut.Replace("\r\n", "\n"));
    }

    /// <summary>One parity program. No expected output — the two backends are each other's.</summary>
    public sealed record ParityProgram(string Name, string Source);

    // ==================================================================
    // THE PROGRAMS. Every one obeys the ten constraints above.
    // ==================================================================

    /// <summary>
    /// The Decimal money battery: 19.99 × 1.08 at full scale, banker's Round, the
    /// 0.1+0.2=0.3 exactness headline, cent accumulation in a loop, scale-preserving
    /// multiplication, exact division, Mod with a NEGATIVE dividend (sign of the
    /// dividend, the .NET rule), compound +=, and unary minus.
    /// </summary>
    private const string DecimalMoneyProgram = @"Module M
 Sub Main()
  Dim price As Decimal = 19.99
  Dim rate As Decimal = 1.08
  Dim total As Decimal = price * rate
  Console.WriteLine(total)
  Dim rounded As Decimal = Decimal.Round(total, 2)
  Console.WriteLine(rounded)
  Dim a As Decimal = 0.1
  Dim b As Decimal = 0.2
  Dim c As Decimal = 0.3
  If a + b = c Then
   Console.WriteLine(""exact"")
  Else
   Console.WriteLine(""inexact"")
  End If
  Dim sum As Decimal = 0
  For i As Integer = 1 To 10
   sum += a
  Next
  Console.WriteLine(sum)
  Dim cents As Decimal = 0
  Dim penny As Decimal = 0.01
  For i As Integer = 1 To 100
   cents += penny
  Next
  Console.WriteLine(cents)
  Dim scaled As Decimal = 1.50
  Dim scaled2 As Decimal = 2.00
  Dim prod As Decimal = scaled * scaled2
  Console.WriteLine(prod)
  Dim q As Decimal = 10
  Dim r As Decimal = 4
  Dim quot As Decimal = q / r
  Console.WriteLine(quot)
  Dim negDividend As Decimal = -7.5
  Dim two As Decimal = 2
  Dim leftover As Decimal = negDividend Mod two
  Console.WriteLine(leftover)
  Dim posDividend As Decimal = 7.5
  Dim leftover2 As Decimal = posDividend Mod two
  Console.WriteLine(leftover2)
  Dim bumped As Decimal = price
  bumped += 1
  Console.WriteLine(bumped)
  Dim minus As Decimal = -price
  Console.WriteLine(minus)
  Console.WriteLine(price.ToString())
  Dim half As Decimal = 2.5
  Dim halfUp As Decimal = 3.5
  Console.WriteLine(Decimal.Round(half, 0))
  Console.WriteLine(Decimal.Round(halfUp, 0))
  Dim truncated As Decimal = Decimal.Truncate(total)
  Console.WriteLine(truncated)
  Dim floored As Decimal = Decimal.Floor(minus)
  Console.WriteLine(floored)
  Dim ceiled As Decimal = Decimal.Ceiling(minus)
  Console.WriteLine(ceiled)
 End Sub
End Module";

    /// <summary>
    /// The conversion round-trips: Double↔Decimal in both directions (CType and CDbl),
    /// Integer→Decimal, and Decimal→integral truncation toward zero in both signs — every
    /// operand ≤15 significant digits and in range (constraint 5). Plus the
    /// ToString/Parse round-trip, which is where a scale bug would surface.
    /// </summary>
    private const string DecimalConversionsProgram = @"Module M
 Sub Main()
  Dim x As Double = 1.5
  Dim d As Decimal = CType(x, Decimal)
  Console.WriteLine(d)
  Dim y As Double = CType(d, Double)
  Console.WriteLine(y)
  Dim price As Decimal = 19.99
  Dim asDouble As Double = CType(price, Double)
  Console.WriteLine(asDouble)
  Dim viaCDbl As Double = CDbl(price)
  Console.WriteLine(viaCDbl)
  Dim seven As Integer = 7
  Dim fromInt As Decimal = CType(seven, Decimal)
  Console.WriteLine(fromInt)
  Dim back As Integer = CType(fromInt, Integer)
  Console.WriteLine(back)
  Dim trunc As Decimal = 2.7
  Dim truncInt As Integer = CType(trunc, Integer)
  Console.WriteLine(truncInt)
  Dim negTrunc As Decimal = -2.7
  Dim negTruncInt As Integer = CType(negTrunc, Integer)
  Console.WriteLine(negTruncInt)
  Dim big As Decimal = 123456789
  Dim asLong As Long = CType(big, Long)
  Console.WriteLine(asLong)
  Dim text As String = price.ToString()
  Console.WriteLine(text)
  Dim reparsed As Decimal = Decimal.Parse(text)
  Console.WriteLine(reparsed)
  Dim scaledText As String = ""1.2500""
  Dim scaledBack As Decimal = Decimal.Parse(scaledText)
  Console.WriteLine(scaledBack)
 End Sub
End Module";

    /// <summary>
    /// The hardest thing to get right in the whole of P1: the from-scratch 96-bit Decimal
    /// engine's DIGIT-EXACTNESS against real .NET. Non-terminating division carried to
    /// 28–29 significant digits with the last digit rounded (1/3, 2/3, 100/7 — 2/3 is the
    /// one where a truncating implementation would end in …6666 instead of …6667), the
    /// MinValue/MaxValue endpoints, high-scale multiplication where the product must be
    /// rounded half-even back into 28 places, and Round at several scales. Anything off by
    /// one ULP anywhere shows up as a character diff.
    /// </summary>
    private const string DecimalPrecisionProgram = @"Module M
 Sub Main()
  Dim one As Decimal = 1
  Dim three As Decimal = 3
  Dim third As Decimal = one / three
  Console.WriteLine(third)
  Dim two As Decimal = 2
  Dim twoThirds As Decimal = two / three
  Console.WriteLine(twoThirds)
  Dim hundred As Decimal = 100
  Dim seven As Decimal = 7
  Dim seventh As Decimal = hundred / seven
  Console.WriteLine(seventh)
  Console.WriteLine(Decimal.MaxValue)
  Console.WriteLine(Decimal.MinValue)
  Console.WriteLine(Decimal.Zero)
  Dim tiny As Decimal = 0.0000000001
  Dim tinyProduct As Decimal = tiny * tiny
  Console.WriteLine(tinyProduct)
  Dim thirdRounded As Decimal = Decimal.Round(third, 4)
  Console.WriteLine(thirdRounded)
  Dim twoThirdsRounded As Decimal = Decimal.Round(twoThirds, 10)
  Console.WriteLine(twoThirdsRounded)
  Dim sumOfThirds As Decimal = third + third + third
  Console.WriteLine(sumOfThirds)
  If sumOfThirds <> one Then
   Console.WriteLine(""thirds-do-not-close"")
  End If
  Dim mixedScale As Decimal = 1.1
  Dim other As Decimal = 2.25
  Console.WriteLine(mixedScale + other)
  Console.WriteLine(mixedScale - other)
  Console.WriteLine(mixedScale * other)
  Dim twelve As Decimal = 12.0
  Dim ten As Decimal = 10.0
  Console.WriteLine(twelve * ten)
  Dim tenInt As Decimal = 10
  Console.WriteLine(twelve * tenInt)
  Console.WriteLine(Decimal.Compare(third, twoThirds))
  Console.WriteLine(Decimal.Compare(twoThirds, third))
  Dim scaledOne As Decimal = 1.00
  If scaledOne = one Then
   Console.WriteLine(""scale-blind-equality"")
  End If
  Console.WriteLine(scaledOne)
  Console.WriteLine(scaledOne.CompareTo(one))
 End Sub
End Module";

    /// <summary>
    /// DateTime from a fixed literal date: every component, DayOfWeek and Kind as NUMBERS
    /// (BL types both Integer; the C# backend must emit the (int) casts or .NET would print
    /// "Tuesday"), the invariant-safe format string, the parameterless ToString() and the
    /// ostream/Console inserter (both culture-sensitive on .NET — see the header),
    /// AddDays, dt−dt → TimeSpan, dt+ts, ordering, and a Parse round-trip.
    /// </summary>
    private const string DateTimeArithmeticProgram = @"Module M
 Sub Main()
  Dim d As New DateTime(2026, 7, 28, 13, 45, 30)
  Console.WriteLine(d.Year)
  Console.WriteLine(d.Month)
  Console.WriteLine(d.Day)
  Console.WriteLine(d.Hour)
  Console.WriteLine(d.Minute)
  Console.WriteLine(d.Second)
  Console.WriteLine(d.DayOfWeek)
  Console.WriteLine(d.DayOfYear)
  Console.WriteLine(d.Kind)
  Console.WriteLine(d.Ticks)
  Console.WriteLine(d.ToString(""yyyy-MM-dd HH:mm:ss""))
  Console.WriteLine(d.ToString(""yyyy-MM-dd""))
  Console.WriteLine(d.ToString(""HH:mm:ss""))
  Console.WriteLine(d.ToString())
  Console.WriteLine(d)
  Dim datePart As DateTime = d.Date
  Console.WriteLine(datePart.ToString(""yyyy-MM-dd HH:mm:ss""))
  Dim later As DateTime = d.AddDays(5)
  Console.WriteLine(later.ToString(""yyyy-MM-dd HH:mm:ss""))
  Dim gap As TimeSpan = later - d
  Console.WriteLine(gap.Days)
  Console.WriteLine(gap.TotalHours)
  Dim two As TimeSpan = TimeSpan.FromHours(2)
  Dim shifted As DateTime = d + two
  Console.WriteLine(shifted.ToString(""yyyy-MM-dd HH:mm:ss""))
  If later > d Then
   Console.WriteLine(""ordered"")
  End If
  Dim parsed As DateTime = DateTime.Parse(""2026-07-28"")
  Console.WriteLine(parsed.ToString(""yyyy-MM-dd HH:mm:ss""))
  Dim sortable As DateTime = DateTime.Parse(""2026-07-28T13:45:30"")
  Console.WriteLine(sortable.ToString(""yyyy-MM-dd HH:mm:ss""))
  Dim general As DateTime = DateTime.Parse(""07/28/2026 13:45:30"")
  Console.WriteLine(general.ToString(""yyyy-MM-dd HH:mm:ss""))
  If sortable = d Then
   Console.WriteLine(""parse-roundtrip"")
  End If
  Console.WriteLine(d.CompareTo(later))
  Console.WriteLine(later.CompareTo(d))
 End Sub
End Module";

    /// <summary>
    /// Calendar edge cases on fixed dates: the AddMonths day CLAMP in a common and a leap
    /// year, Feb 29 → Feb 28 on AddYears, DayOfYear at a year boundary, day/hour/minute/
    /// second rollovers in both directions, and the ordering operators.
    /// </summary>
    private const string DateTimeCalendarProgram = @"Module M
 Sub Main()
  Dim jan31 As New DateTime(2026, 1, 31)
  Dim feb As DateTime = jan31.AddMonths(1)
  Console.WriteLine(feb.ToString(""yyyy-MM-dd""))
  Dim leapJan31 As New DateTime(2028, 1, 31)
  Dim leapFeb As DateTime = leapJan31.AddMonths(1)
  Console.WriteLine(leapFeb.ToString(""yyyy-MM-dd""))
  Dim leapDay As New DateTime(2028, 2, 29)
  Console.WriteLine(leapDay.DayOfYear)
  Console.WriteLine(leapDay.DayOfWeek)
  Dim afterLeap As DateTime = leapDay.AddYears(1)
  Console.WriteLine(afterLeap.ToString(""yyyy-MM-dd""))
  Console.WriteLine(DateTime.DaysInMonth(2028, 2))
  Console.WriteLine(DateTime.DaysInMonth(2026, 2))
  Dim endOfYear As New DateTime(2026, 12, 31, 23, 0, 0)
  Console.WriteLine(endOfYear.DayOfYear)
  Dim rolled As DateTime = endOfYear.AddHours(2)
  Console.WriteLine(rolled.ToString(""yyyy-MM-dd HH:mm:ss""))
  Dim rolledBack As DateTime = rolled.AddDays(-1)
  Console.WriteLine(rolledBack.ToString(""yyyy-MM-dd HH:mm:ss""))
  Dim minutes As DateTime = endOfYear.AddMinutes(90)
  Console.WriteLine(minutes.ToString(""yyyy-MM-dd HH:mm:ss""))
  Dim seconds As DateTime = endOfYear.AddSeconds(-1)
  Console.WriteLine(seconds.ToString(""yyyy-MM-dd HH:mm:ss""))
  Dim millis As DateTime = endOfYear.AddMilliseconds(1500)
  Console.WriteLine(millis.ToString(""yyyy-MM-dd HH:mm:ss""))
  Dim byTicks As DateTime = endOfYear.AddTicks(10000000)
  Console.WriteLine(byTicks.ToString(""yyyy-MM-dd HH:mm:ss""))
  Dim span As TimeSpan = rolled - endOfYear
  Console.WriteLine(span.TotalHours)
  If jan31 < feb Then
   Console.WriteLine(""lt"")
  End If
  If feb <> jan31 Then
   Console.WriteLine(""ne"")
  End If
  Dim sameAgain As New DateTime(2026, 1, 31)
  If sameAgain = jan31 Then
   Console.WriteLine(""eq"")
  End If
 End Sub
End Module";

    /// <summary>
    /// DEFAULT-VALUE parity: an UNINITIALIZED local of each value type. The C# backend
    /// emits <c>default!</c>; the C++ backend zero-initializes with <c>{}</c>. This is also
    /// the fixture's culture-forcing self-check — <c>Console.WriteLine(d)</c> on a default
    /// DateTime prints "01/01/0001 00:00:00" under invariant and "1/1/0001 12:00:00 AM"
    /// under en-US, so a broken env var fails HERE, loudly.
    /// </summary>
    private const string DefaultValuesProgram = @"Module M
 Sub Main()
  Dim d As DateTime
  Console.WriteLine(d)
  Console.WriteLine(d.ToString())
  Console.WriteLine(d.Year)
  Console.WriteLine(d.Month)
  Console.WriteLine(d.Day)
  Console.WriteLine(d.Ticks)
  Console.WriteLine(d.ToString(""yyyy-MM-dd HH:mm:ss""))
  Dim ts As TimeSpan
  Console.WriteLine(ts.ToString())
  Console.WriteLine(ts.Ticks)
  Console.WriteLine(ts.TotalSeconds)
  Dim g As Guid
  Console.WriteLine(g.ToString())
  Dim dec0 As Decimal
  Console.WriteLine(dec0)
  Console.WriteLine(dec0.ToString())
  Dim dto As DateTimeOffset
  Console.WriteLine(dto.ToString())
  Dim minDate As DateTime = DateTime.MinValue
  If minDate = d Then
   Console.WriteLine(""min-is-default"")
  End If
  Dim zeroSpan As TimeSpan = TimeSpan.Zero
  If zeroSpan = ts Then
   Console.WriteLine(""zero-is-default"")
  End If
  Dim emptyGuid As Guid = Guid.Empty
  If emptyGuid = g Then
   Console.WriteLine(""empty-is-default"")
  End If
 End Sub
End Module";

    /// <summary>
    /// TimeSpan: the FromX factories, the components-vs-totals distinction
    /// (Hours=1 / TotalHours=1.5), the 3- and 4-argument constructors, Ticks, Zero,
    /// unary minus, compound +=, and the invariant "c" ToString incl. its 7-digit fraction.
    /// </summary>
    private const string TimeSpanProgram = @"Module M
 Sub Main()
  Dim ts As TimeSpan = TimeSpan.FromMinutes(90)
  Console.WriteLine(ts.Hours)
  Console.WriteLine(ts.Minutes)
  Console.WriteLine(ts.Seconds)
  Console.WriteLine(ts.Days)
  Console.WriteLine(ts.TotalHours)
  Console.WriteLine(ts.TotalMinutes)
  Console.WriteLine(ts.TotalSeconds)
  Console.WriteLine(ts.TotalDays)
  Console.WriteLine(ts.Ticks)
  Console.WriteLine(ts.ToString())
  Dim ts2 As TimeSpan = TimeSpan.FromSeconds(90)
  Console.WriteLine(ts2.ToString())
  Dim added As TimeSpan = ts
  added += ts2
  Console.WriteLine(added.ToString())
  Console.WriteLine(added)
  Dim full As New TimeSpan(1, 2, 30, 15)
  Console.WriteLine(full.Days)
  Console.WriteLine(full.Hours)
  Console.WriteLine(full.Minutes)
  Console.WriteLine(full.Seconds)
  Console.WriteLine(full.ToString())
  Dim hm As New TimeSpan(2, 15, 0)
  Console.WriteLine(hm.ToString())
  Console.WriteLine(hm.Ticks)
  Dim zero As TimeSpan = TimeSpan.Zero
  Console.WriteLine(zero.ToString())
  Dim couple As TimeSpan = TimeSpan.FromDays(2)
  Console.WriteLine(couple.TotalHours)
  Dim negated As TimeSpan = -hm
  Console.WriteLine(negated.ToString())
  Console.WriteLine(negated.TotalMinutes)
  Dim millis As TimeSpan = TimeSpan.FromMilliseconds(1500)
  Console.WriteLine(millis.ToString())
  Dim fromTicks As TimeSpan = TimeSpan.FromTicks(864000000000)
  Console.WriteLine(fromTicks.ToString())
  Dim difference As TimeSpan = hm - ts
  Console.WriteLine(difference.ToString())
  If ts < hm Then
   Console.WriteLine(""shorter"")
  End If
  Console.WriteLine(ts.CompareTo(hm))
 End Sub
End Module";

    /// <summary>
    /// Guid on FIXED values only (constraint 8): Parse → ToString round-trip, the
    /// <c>New Guid("…")</c> string constructor agreeing with Parse, Guid.Empty, equality,
    /// the inserter, and the "D" form's length.
    /// </summary>
    private const string GuidProgram = @"Module M
 Sub Main()
  Dim known As Guid = Guid.Parse(""6f9619ff-8b86-d011-b42d-00c04fc964ff"")
  Console.WriteLine(known.ToString())
  Console.WriteLine(known)
  Dim text As String = known.ToString()
  Console.WriteLine(text.Length)
  Dim built As New Guid(""6f9619ff-8b86-d011-b42d-00c04fc964ff"")
  Console.WriteLine(built.ToString())
  If built = known Then
   Console.WriteLine(""ctor-eq"")
  End If
  Dim other As Guid = Guid.Parse(""00112233-4455-6677-8899-aabbccddeeff"")
  Console.WriteLine(other.ToString())
  If other <> known Then
   Console.WriteLine(""distinct"")
  End If
  Dim empty As Guid = Guid.Empty
  Console.WriteLine(empty.ToString())
  Dim upper As Guid = Guid.Parse(""6F9619FF-8B86-D011-B42D-00C04FC964FF"")
  Console.WriteLine(upper.ToString())
  If upper = known Then
   Console.WriteLine(""case-insensitive-parse"")
  End If
  Dim roundTripped As Guid = Guid.Parse(text)
  Console.WriteLine(roundTripped.ToString())
 End Sub
End Module";

    /// <summary>
    /// StringBuilder — the one reference type. The overload pin (<c>Append(anInteger)</c>
    /// appends "42", <c>Append(aBoolean)</c> appends "True" not "1"), the fluent chain
    /// returning the SAME builder, Length in bytes (ASCII here, so UTF-8 == UTF-16 —
    /// spec §9's divergence is not exercised), Insert/Remove/Replace/Clear, the inserter,
    /// and the ALIASING contract: two BL names for one builder see each other's mutations.
    /// Per constraint 7, Length is never printed after an AppendLine.
    /// </summary>
    private const string StringBuilderProgram = @"Module M
 Sub Main()
  Dim sb As New StringBuilder()
  sb.Append(""count="")
  sb.Append(42)
  sb.Append("" flag="")
  sb.Append(True)
  Console.WriteLine(sb.ToString())
  Console.WriteLine(sb.Length)
  Dim other As StringBuilder = sb
  other.Append(""!"")
  Console.WriteLine(sb.ToString())
  Console.WriteLine(other.ToString())
  Console.WriteLine(sb.Length)
  sb.Clear()
  sb.Append(""a"")
  Console.WriteLine(other.ToString())
  Console.WriteLine(other.Length)
  Dim chain As New StringBuilder(""seed:"")
  chain.Append(""x"").Append(7).Append(""y"")
  Console.WriteLine(chain.ToString())
  Console.WriteLine(chain)
  Console.WriteLine(chain.Length)
  Dim edit As New StringBuilder(""hello world"")
  edit.Insert(5, "","")
  Console.WriteLine(edit.ToString())
  edit.Remove(0, 1)
  Console.WriteLine(edit.ToString())
  edit.Replace(""world"", ""there"")
  Console.WriteLine(edit.ToString())
  Console.WriteLine(edit.Length)
  Dim numbers As New StringBuilder()
  For i As Integer = 1 To 5
   numbers.Append(i)
   numbers.Append("","")
  Next
  Console.WriteLine(numbers.ToString())
  Console.WriteLine(numbers.Length)
 End Sub
End Module";

    /// <summary>
    /// DateTimeOffset on fixed clocks: the (clock, offset) constructor, the wall-clock vs
    /// UTC split, Offset, the Unix-epoch conversions, FromUnixTimeSeconds, ToOffset, and the
    /// headline UTC-INSTANT equality — 12:00−05:00 equals 17:00+00:00 though the wall clocks
    /// differ. ToString() is invariant on both sides only because of the forced culture.
    /// </summary>
    private const string DateTimeOffsetProgram = @"Module M
 Sub Main()
  Dim clock As New DateTime(2026, 7, 28, 12, 0, 0)
  Dim off As TimeSpan = TimeSpan.FromHours(-5)
  Dim dto As New DateTimeOffset(clock, off)
  Console.WriteLine(dto.ToString())
  Console.WriteLine(dto)
  Dim wall As DateTime = dto.DateTime
  Console.WriteLine(wall.ToString(""yyyy-MM-dd HH:mm:ss""))
  Dim utc As DateTime = dto.UtcDateTime
  Console.WriteLine(utc.ToString(""yyyy-MM-dd HH:mm:ss""))
  Dim shownOffset As TimeSpan = dto.Offset
  Console.WriteLine(shownOffset.TotalHours)
  Console.WriteLine(shownOffset.ToString())
  Console.WriteLine(dto.ToUnixTimeSeconds())
  Console.WriteLine(dto.ToUnixTimeMilliseconds())
  Console.WriteLine(dto.Ticks)
  Dim clock2 As New DateTime(2026, 7, 28, 17, 0, 0)
  Dim dto2 As New DateTimeOffset(clock2, TimeSpan.Zero)
  If dto = dto2 Then
   Console.WriteLine(""same-instant"")
  End If
  If dto <> dto2 Then
   Console.WriteLine(""NOT-same-instant"")
  End If
  Console.WriteLine(dto.CompareTo(dto2))
  Dim epoch As DateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(0)
  Console.WriteLine(epoch.ToString())
  Dim epochMs As DateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(1500)
  Console.WriteLine(epochMs.ToString())
  Dim shifted As DateTimeOffset = dto.ToOffset(TimeSpan.Zero)
  Console.WriteLine(shifted.ToString())
  Dim positive As New DateTimeOffset(clock, TimeSpan.FromHours(5))
  Console.WriteLine(positive.ToString())
  Console.WriteLine(positive.ToUnixTimeSeconds())
 End Sub
End Module";

    /// <summary>
    /// SByte/Byte numeric PRINT parity (spec §14.6): both backends must print the NUMBER,
    /// not the character — a raw <c>int8_t</c>/<c>uint8_t</c> would stream as a glyph on C++
    /// where .NET prints digits. Also covers SByte arithmetic, ordering, compound +=,
    /// the range endpoints, and the explicit widening BL requires (implicit SByte→Integer
    /// is a front-end error).
    /// </summary>
    private const string SByteByteProgram = @"Module M
 Sub Main()
  Dim b As Byte = 65
  Console.WriteLine(b)
  Dim s As SByte = -3
  Console.WriteLine(s)
  Dim lo As SByte = -3
  Dim hi As SByte = 10
  Dim sum As SByte = lo + hi
  Console.WriteLine(sum)
  Dim diff As SByte = lo - hi
  Console.WriteLine(diff)
  Dim bumped As SByte = lo
  bumped += 5
  Console.WriteLine(bumped)
  If lo < hi Then
   Console.WriteLine(""ordered"")
  End If
  If lo <> hi Then
   Console.WriteLine(""ne"")
  End If
  Dim widened As Integer = CType(hi, Integer)
  Console.WriteLine(widened * 10)
  Dim maxS As SByte = 127
  Console.WriteLine(maxS)
  Dim minS As SByte = -128
  Console.WriteLine(minS)
  Dim maxB As Byte = 255
  Console.WriteLine(maxB)
  Dim zeroB As Byte = 0
  Console.WriteLine(zeroB)
  Dim widenedByte As Integer = CType(maxB, Integer)
  Console.WriteLine(widenedByte + 1)
  Dim narrowed As SByte = CType(widened, SByte)
  Console.WriteLine(narrowed)
 End Sub
End Module";

    /// <summary>
    /// The VB date stdlib (spec §7) on FIXED dates: Year/Month/Day/Hour/Minute/Second,
    /// FormatDate, DateAdd over every interval part (incl. the month CLAMP, the
    /// case-insensitive match and the unrecognized-interval no-op), and DateDiff in both
    /// directions. The repo's argument order — DateAdd(date, interval, number),
    /// DateDiff(date1, date2, interval) — not classic VB's. Now()/Today()/NewGuid() are
    /// excluded by constraint 8; CppBclEndToEndTests covers them structurally.
    /// </summary>
    private const string StdlibDatesProgram = @"Module M
 Sub Main()
  Dim d As New DateTime(2026, 7, 28, 13, 45, 30)
  Console.WriteLine(Year(d))
  Console.WriteLine(Month(d))
  Console.WriteLine(Day(d))
  Console.WriteLine(Hour(d))
  Console.WriteLine(Minute(d))
  Console.WriteLine(Second(d))
  Console.WriteLine(FormatDate(d, ""yyyy-MM-dd""))
  Console.WriteLine(FormatDate(d, ""yyyy-MM-dd HH:mm:ss""))
  Console.WriteLine(FormatDate(d, ""HH:mm:ss""))
  Dim byDays As DateTime = DateAdd(d, ""d"", 5)
  Console.WriteLine(FormatDate(byDays, ""yyyy-MM-dd""))
  Dim byMonths As DateTime = DateAdd(d, ""m"", 5)
  Console.WriteLine(FormatDate(byMonths, ""yyyy-MM-dd""))
  Dim byYears As DateTime = DateAdd(d, ""y"", 5)
  Console.WriteLine(FormatDate(byYears, ""yyyy-MM-dd""))
  Dim byHours As DateTime = DateAdd(d, ""h"", 5)
  Console.WriteLine(FormatDate(byHours, ""HH:mm:ss""))
  Dim byMinutes As DateTime = DateAdd(d, ""n"", 5)
  Console.WriteLine(FormatDate(byMinutes, ""HH:mm:ss""))
  Dim bySeconds As DateTime = DateAdd(d, ""s"", 5)
  Console.WriteLine(FormatDate(bySeconds, ""HH:mm:ss""))
  Dim negativeDays As DateTime = DateAdd(d, ""d"", -10)
  Console.WriteLine(FormatDate(negativeDays, ""yyyy-MM-dd""))
  Dim clamped As New DateTime(2026, 1, 31)
  Dim clampedNext As DateTime = DateAdd(clamped, ""m"", 1)
  Console.WriteLine(FormatDate(clampedNext, ""yyyy-MM-dd""))
  Dim upperInterval As DateTime = DateAdd(d, ""D"", 5)
  Console.WriteLine(FormatDate(upperInterval, ""yyyy-MM-dd""))
  Dim unknownInterval As DateTime = DateAdd(d, ""zz"", 5)
  Console.WriteLine(FormatDate(unknownInterval, ""yyyy-MM-dd HH:mm:ss""))
  Dim later As New DateTime(2027, 9, 30, 20, 15, 45)
  Console.WriteLine(DateDiff(d, later, ""d""))
  Console.WriteLine(DateDiff(d, later, ""m""))
  Console.WriteLine(DateDiff(d, later, ""y""))
  Console.WriteLine(DateDiff(d, later, ""h""))
  Console.WriteLine(DateDiff(d, later, ""n""))
  Console.WriteLine(DateDiff(d, later, ""s""))
  Console.WriteLine(DateDiff(d, later, ""S""))
  Console.WriteLine(DateDiff(d, later, ""zz""))
  Console.WriteLine(DateDiff(later, d, ""d""))
 End Sub
End Module";

    /// <summary>
    /// Spec §11 end-to-end, cross-backend: a BL Try/Catch around a Decimal divide-by-zero
    /// and a malformed Guid.Parse must catch on BOTH backends and print the same markers
    /// (native <c>std::runtime_error</c> vs .NET DivideByZeroException/FormatException).
    /// Neither throw is reachable at compile time — the IR constant folder deliberately
    /// never folds Decimal constants — so the division survives the optimizer into both
    /// emitted programs. The programs exit 0 on both sides; a non-caught throw would fail
    /// the harness's exit-code assert before the diff even ran.
    /// </summary>
    private const string TryCatchProgram = @"Module M
 Sub Main()
  Dim one As Decimal = 1
  Dim zero As Decimal = 0
  Try
   Dim r As Decimal = one / zero
   Console.WriteLine(r)
  Catch ex As Exception
   Console.WriteLine(""CAUGHT-DIV0"")
  End Try
  Try
   Dim bad As Guid = Guid.Parse(""not-a-guid"")
   Console.WriteLine(bad.ToString())
  Catch ex As Exception
   Console.WriteLine(""CAUGHT-GUID"")
  End Try
  Try
   Dim badDec As Decimal = Decimal.Parse(""nonsense"")
   Console.WriteLine(badDec)
  Catch ex As Exception
   Console.WriteLine(""CAUGHT-DECIMAL-PARSE"")
  End Try
  Try
   Dim ok As Decimal = 4
   Dim by As Decimal = 2
   Dim fine As Decimal = ok / by
   Console.WriteLine(fine)
  Catch ex As Exception
   Console.WriteLine(""UNEXPECTED"")
  End Try
  Console.WriteLine(""after"")
 End Sub
End Module";

    /// <summary>
    /// Spec §11.1 WITHOUT a .NET call — the native half of P2a-2 plan programs 2 and 3. Pins a
    /// local <c>Throw</c> caught by a SUBCLASS clause (<c>ArgumentNullException</c> caught by
    /// <c>Catch e As ArgumentException</c>), an EXACT-type clause, a non-throwing <c>Try</c>,
    /// and real CONTROL FLOW inside a catch body (an <c>If</c> plus a <c>For</c>).
    ///
    /// <para>The control flow is the point: the C++ backend emits the
    /// <c>catch (const BasicLang::NetException&amp;)</c> ladder even for a program with no .NET
    /// surface, so each catch body is emitted TWICE and C++ labels are function-scoped — the
    /// ladder's copies must carry the <c>_nex</c> suffix or the TU fails to compile on a
    /// redefined label.</para>
    ///
    /// <para>The <c>If</c> condition comes from a Decimal division rather than a literal
    /// because the IR constant folder never folds Decimal constants; a literal condition folds
    /// to <c>if (true)</c> and stops being a live runtime decision.</para>
    ///
    /// <para>⛔ <b>WHAT THIS ROW DOES NOT PROVE, deliberately.</b> On the C++ backend every
    /// non-<c>Exception</c> clause lowers to <c>catch (const std::runtime_error&amp;)</c> — and
    /// <c>Visit(IRThrow)</c> throws a bare <c>std::runtime_error</c> carrying only a message —
    /// so clause 1 catches EVERYTHING and no type discrimination exists. This program agrees on
    /// both legs because the thrown type genuinely matches clause 1: it is a POSITIVE-match
    /// test only. The discriminating case (a thrown type that must SKIP clause 1) currently
    /// diverges — measured: <c>Guid.Parse("not-a-guid")</c> against a
    /// <c>Catch e As InvalidOperationException</c> prints IOE natively where .NET prints EX —
    /// and is chipped as <c>task_d7f0a91c</c> rather than pinned here, because the row would be
    /// red until the backend carries an exception's type at runtime.</para>
    /// </summary>
    private const string LocalThrowMultiCatchProgram = @"Module M
 Sub Main()
  Dim one As Decimal = 1
  Dim two As Decimal = 2
  Dim half As Decimal = one / two
  Try
   Throw New ArgumentNullException(""boom"")
  Catch e As ArgumentException
   Console.WriteLine(""SUBCLASS-MATCH"")
   If half < one Then
    Console.WriteLine(""catch-branch-then"")
   Else
    Console.WriteLine(""catch-branch-else"")
   End If
   For i As Integer = 1 To 3
    Console.WriteLine(i)
   Next
  Catch b As Exception
   Console.WriteLine(""EX"")
  End Try
  Try
   Console.WriteLine(""no-throw"")
  Catch e As ArgumentException
   Console.WriteLine(""AE2"")
  Catch b As Exception
   Console.WriteLine(""EX2"")
  End Try
  Try
   Throw New ArgumentException(""plain"")
  Catch e As ArgumentException
   Console.WriteLine(""EXACT-MATCH"")
  Catch b As Exception
   Console.WriteLine(""EX3"")
  End Try
  Console.WriteLine(""after"")
 End Sub
End Module";

    // ------------------------------------------------------------------
    // THE TABLE. One row per program; there is no expected-output column.
    // ------------------------------------------------------------------

    private static readonly ParityProgram[] Programs =
    {
        new ParityProgram("DecimalMoney", DecimalMoneyProgram),
        new ParityProgram("DecimalConversions", DecimalConversionsProgram),
        new ParityProgram("DecimalPrecision", DecimalPrecisionProgram),
        new ParityProgram("DateTimeArithmetic", DateTimeArithmeticProgram),
        new ParityProgram("DateTimeCalendar", DateTimeCalendarProgram),
        new ParityProgram("DefaultValues", DefaultValuesProgram),
        new ParityProgram("TimeSpan", TimeSpanProgram),
        new ParityProgram("Guid", GuidProgram),
        new ParityProgram("StringBuilder", StringBuilderProgram),
        new ParityProgram("DateTimeOffset", DateTimeOffsetProgram),
        new ParityProgram("SByteByte", SByteByteProgram),
        new ParityProgram("StdlibDates", StdlibDatesProgram),
        new ParityProgram("TryCatch", TryCatchProgram),
        new ParityProgram("LocalThrowMultiCatch", LocalThrowMultiCatchProgram),
    };

    public static IEnumerable<TestCaseData> ParityCases() =>
        Programs.Select(p => new TestCaseData(p).SetArgDisplayNames(p.Name));

    /// <summary>
    /// THE ORACLE. Real .NET on the left, the native runtime on the right, nothing
    /// hand-written in between.
    /// </summary>
    [TestCaseSource(nameof(ParityCases))]
    public void Backends_PrintIdenticalOutput(ParityProgram program)
    {
        var (csharp, cpp) = RunBothBackends(program.Source);

        // Vacuity guard: two identically-EMPTY stdouts satisfy the equality assert while
        // proving nothing. Every program prints, so an empty oracle side means the C# leg
        // silently produced no output (a swallowed build/run failure), not parity. This is
        // a liveness check on the harness, NOT a hand-written expectation — asserting the
        // CONTENT here would reintroduce the expected column this fixture exists to avoid.
        Assert.That(csharp, Is.Not.Empty,
            $"'{program.Name}': the C# (oracle) leg produced NO stdout — the parity comparison " +
            $"would be vacuous. Suspect a silently-failed build or run in CliTestHarness.");

        Assert.That(cpp, Is.EqualTo(csharp),
            csharp == cpp ? "" :
            $"BACKEND PARITY DIVERGENCE in '{program.Name}' — real .NET is the oracle, so this is a " +
            $"semantic defect in the native runtime unless spec §9/§13 documents it (in which case the " +
            $"PROGRAM violates a fixture constraint).\n" +
            BclE2E.DescribeFirstDifference(csharp, cpp, "C# backend (.NET)", "C++ backend (native)"));
    }
}
