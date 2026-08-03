using System.Reflection;
using System.Runtime.ExceptionServices;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.Net;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Spec §6.4's conversion pairs (P2a-2 Task 6): the six P1 <c>NativeOwned</c> value types
/// bridged between their native layouts and the real .NET types, on BOTH sides of the wire.
///
/// <para><b>THE ORACLE RULE.</b> §6.4: "the failure mode is a silently wrong value rather than
/// a crash". Every test here pins BIT PATTERNS against hard-coded vectors computed from real
/// .NET (the fixture re-derives each literal from the BCL right next to the pin, so a stale
/// literal fails loudly) — never round-trip-only tests, because a symmetric bug passes a round
/// trip.</para>
///
/// <para><b>The recorded P1 layouts (wire forms are these, VERBATIM):</b></para>
/// <list type="bullet">
/// <item><description><b>DateTime</b> (CppBclRuntime.cs:232-245): <c>uint64_t dateData_</c> =
/// ticks in the low 62 bits (<c>TicksMask = 0x3FFFFFFFFFFFFFFF</c>, :235) | kind in the top 2
/// (<c>Kind() = dateData_ &gt;&gt; 62</c>, :245), kind values Unspecified=0/Utc=1/Local=2
/// (:236) — numerically identical to <c>System.DateTimeKind</c> and to .NET's internal
/// dateData flags for Utc/Unspecified, so <c>DateTime.ToBinary()</c> equals the wire for
/// those two kinds. For Local, <c>ToBinary()</c> does a UTC adjustment the wire deliberately
/// does NOT (the wire is the raw {ticks, kind} pair; the managed side reconstructs via the
/// <c>DateTime(long, DateTimeKind)</c> constructor, never <c>FromBinary</c>).</description></item>
/// <item><description><b>TimeSpan</b> (CppBclRuntime.cs:170-171): <c>int64_t ticks_</c>.</description></item>
/// <item><description><b>Decimal</b> (CppDecimalRuntime.cs:200-207): <c>uint32_t lo_, mid_,
/// hi_, flags_</c>; flags = scale (0-28) in bits 16-23, sign in bit 31, all other bits zero;
/// <c>decimal.GetBits</c> order == {lo, mid, hi, flags} (:203). DOCUMENTED DIVERGENCE (:203-205):
/// the native type canonicalizes -0 to +0, so a .NET negative-zero crossing INBOUND loses its
/// sign flag (real .NET preserves it through GetBits).</description></item>
/// <item><description><b>Guid</b> (CppBclRuntime.cs:308-324): <c>int32_t a_; int16_t b_, c_;
/// uint8_t d_[8]</c> — .NET's field layout. Wire = the 16 <c>ToByteArray()</c> bytes
/// (:321, :728-735): a, b, c little-endian BY SHIFTS (host-endianness-independent), then d_
/// verbatim — byte-for-byte what <c>System.Guid.ToByteArray()</c> produces (mixed-endian vs
/// the canonical string).</description></item>
/// <item><description><b>DateTimeOffset</b> (CppBclRuntime.cs:326-329): <c>DateTime utc_</c>
/// (the UTC instant, KindUnspecified) + <c>int16_t offsetMinutes_</c> (validated ±14h whole
/// minutes, :157-165). Wire = {utcTicks int64, offsetMinutes int16}; .NET's ctor takes CLOCK
/// ticks, so the managed side adds the offset back on reconstruction.</description></item>
/// <item><description><b>StringBuilder</b> (CppBclRuntime.cs:356-383): crosses BY VALUE as
/// String — ONE direction only (native → managed), per §6.4's table row. No managed → native
/// conversion exists on purpose.</description></item>
/// </list>
///
/// <para><b>Fixture shape.</b> FAST half: the managed helpers are compiled from
/// <see cref="BlnetShimSources.MarshalCs"/> IN-TEST (Roslyn emit — the same artifact text the
/// generated shim ships, not a re-implementation) and invoked through reflection. INTEGRATION
/// half: native programs include <c>blnet_marshal.hpp</c> after the two P1 headers (the
/// include-order contract on <see cref="CppNetMarshal"/>) and print value-independent
/// <c>name=%d</c> markers, per the CppBclRuntimeTests convention.</para>
/// </summary>
[TestFixture]
public class NetConversionPairTests
{
    // ======================================================================================
    // Hard-coded wire vectors, computed from real .NET (provenance re-asserted per test).
    // ======================================================================================

    // DateTime: 2026-08-02T00:00:00, ticks = 639212256000000000 = 0x08DEF028FEF4C000.
    private const long Ticks20260802 = 639212256000000000;
    private const ulong WireUtc20260802 = 0x48DEF028FEF4C000UL;      // ticks | (Utc=1)   << 62
    private const ulong WireUnspec20260802 = 0x08DEF028FEF4C000UL;   // ticks | (Unspec=0)<< 62
    private const ulong WireLocal20260802 = 0x88DEF028FEF4C000UL;    // ticks | (Local=2) << 62
    private const long MaxTicks = 3155378975999999999;               // 9999-12-31T23:59:59.9999999
    private const ulong WireMaxLocal = 0xABCA2875F4373FFFUL;         // MaxTicks | (Local=2) << 62

    // TimeSpan: 1.02:03:04.5000000.
    private const long TsNominalTicks = 937845000000;

    // Decimal (GetBits order lo, mid, hi, flags).
    private const int DecFlagsScale2 = 0x00020000;                          // 123.45m
    private const int DecFlagsNegScale6 = unchecked((int)0x80060000);       // -0.000001m
    private const int DecFlagsNegZero = unchecked((int)0x80000000);         // new decimal(0,0,0,true,0)

    // Guid vectors (ToByteArray order: a,b,c little-endian then d verbatim).
    private const string GuidNominal = "00112233-4455-6677-8899-aabbccddeeff";
    private static readonly byte[] GuidNominalWire =
    {
        0x33, 0x22, 0x11, 0x00, 0x55, 0x44, 0x77, 0x66,
        0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF,
    };
    private const string GuidHighBit = "ffeeddcc-bbaa-9988-7766-554433221100";
    private static readonly byte[] GuidHighBitWire =
    {
        0xCC, 0xDD, 0xEE, 0xFF, 0xAA, 0xBB, 0x88, 0x99,
        0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, 0x00,
    };

    // DateTimeOffset: both vectors share clock 2026-08-02T10:30:00 (ticks 639212634000000000).
    private const long DtoClockTicks = 639212634000000000;
    private const long DtoUtcTicksPlus2h = 639212562000000000;       // clock - 120 min
    private const short DtoOffsetPlus2h = 120;
    private const long DtoUtcTicksMinus530 = 639212832000000000;     // clock + 330 min
    private const short DtoOffsetMinus530 = -330;

    // ======================================================================================
    // FAST: managed side — the shipped BlnetMarshal.cs source, compiled in-test.
    // ======================================================================================

    private static Type _marshal = null!;

    [OneTimeSetUp]
    public void CompileManagedHelpers()
    {
        var compilation = CSharpCompilation.Create(
            "BlnetMarshalProbe",
            new[] { CSharpSyntaxTree.ParseText(BlnetShimSources.MarshalCs) },
            NetTypeResolverTestRefs.FrameworkPaths.Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        Assert.That(emit.Success, Is.True,
            "BlnetShimSources.MarshalCs does not compile — every generated shim would fail its "
            + "publish:\n" + string.Join("\n",
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var type = Assembly.Load(ms.ToArray()).GetType("BlnetTestShim.BlnetMarshal");
        Assert.That(type, Is.Not.Null,
            "MarshalCs compiled but declares no BlnetTestShim.BlnetMarshal class.");
        _marshal = type!;
    }

    /// <summary>Invokes a BlnetMarshal helper, unwrapping TargetInvocationException so the
    /// helper's own exception type is assertable. A MISSING helper is the step-1 red.</summary>
    private static object? Call(string method, params object?[] args)
    {
        var mi = RequireHelper(method);
        try
        {
            return mi.Invoke(null, args);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw; // unreachable
        }
    }

    private static MethodInfo RequireHelper(string method)
    {
        var mi = _marshal.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
        Assert.That(mi, Is.Not.Null,
            $"BlnetMarshal.{method} is missing from BlnetShimSources.MarshalCs — the managed "
            + "half of the §6.4 conversion pair does not exist.");
        return mi!;
    }

    [Test]
    public void Managed_DateTime_WireVectors()
    {
        // Provenance: the pinned literals ARE real .NET's numbers.
        Assert.Multiple(() =>
        {
            Assert.That(new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc).Ticks,
                Is.EqualTo(Ticks20260802), "stale vector: ticks");
            Assert.That(new DateTime(Ticks20260802, DateTimeKind.Utc).ToBinary(),
                Is.EqualTo((long)WireUtc20260802),
                "stale vector: for Utc the wire IS .NET's ToBinary dateData");
            Assert.That(new DateTime(Ticks20260802, DateTimeKind.Unspecified).ToBinary(),
                Is.EqualTo(unchecked((long)WireUnspec20260802)),
                "stale vector: for Unspecified the wire IS .NET's ToBinary dateData");
            Assert.That(DateTime.MaxValue.Ticks, Is.EqualTo(MaxTicks), "stale vector: MaxTicks");
        });

        // Wire -> managed: ticks and Kind reconstructed from the exact bit positions.
        var utc = (DateTime)Call("DateTimeFromWire", WireUtc20260802)!;
        var unspec = (DateTime)Call("DateTimeFromWire", WireUnspec20260802)!;
        var local = (DateTime)Call("DateTimeFromWire", WireLocal20260802)!;
        var maxLocal = (DateTime)Call("DateTimeFromWire", WireMaxLocal)!;
        Assert.Multiple(() =>
        {
            Assert.That((utc.Ticks, utc.Kind), Is.EqualTo((Ticks20260802, DateTimeKind.Utc)));
            Assert.That((unspec.Ticks, unspec.Kind), Is.EqualTo((Ticks20260802, DateTimeKind.Unspecified)));
            Assert.That((local.Ticks, local.Kind), Is.EqualTo((Ticks20260802, DateTimeKind.Local)));
            Assert.That((maxLocal.Ticks, maxLocal.Kind), Is.EqualTo((MaxTicks, DateTimeKind.Local)));
        });

        // Managed -> wire: all three Kind variants against the pinned bit patterns.
        Assert.Multiple(() =>
        {
            Assert.That(Call("DateTimeToWire", new DateTime(Ticks20260802, DateTimeKind.Utc)),
                Is.EqualTo(WireUtc20260802));
            Assert.That(Call("DateTimeToWire", new DateTime(Ticks20260802, DateTimeKind.Unspecified)),
                Is.EqualTo(WireUnspec20260802));
            Assert.That(Call("DateTimeToWire", new DateTime(Ticks20260802, DateTimeKind.Local)),
                Is.EqualTo(WireLocal20260802));
            Assert.That(Call("DateTimeToWire", new DateTime(MaxTicks, DateTimeKind.Local)),
                Is.EqualTo(WireMaxLocal));
        });

        // Kind bits 3 do not exist in live .NET DateTimes; the reconstruction must refuse
        // rather than fabricate a Kind (mirrors native FromTicksAndKind's range check).
        Assert.Throws<ArgumentException>(() => Call("DateTimeFromWire", (3UL << 62) | 1UL));
    }

    [Test]
    public void Managed_TimeSpan_WireVectors()
    {
        Assert.That(new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(5000000)).Ticks,
            Is.EqualTo(TsNominalTicks), "stale vector: 1.02:03:04.5000000");

        Assert.Multiple(() =>
        {
            Assert.That(Call("TimeSpanFromWire", TsNominalTicks),
                Is.EqualTo(new TimeSpan(1, 2, 3, 4) + TimeSpan.FromTicks(5000000)));
            Assert.That(Call("TimeSpanToWire", TimeSpan.FromTicks(TsNominalTicks)),
                Is.EqualTo(TsNominalTicks));
            // Negative and extreme edges.
            Assert.That(Call("TimeSpanFromWire", -1L), Is.EqualTo(TimeSpan.FromTicks(-1)));
            Assert.That(Call("TimeSpanToWire", TimeSpan.MinValue), Is.EqualTo(long.MinValue));
        });
    }

    [Test]
    public void Managed_Decimal_WireVectors()
    {
        // Provenance: GetBits really is {lo, mid, hi, flags} with these exact values.
        Assert.Multiple(() =>
        {
            Assert.That(decimal.GetBits(123.45m), Is.EqualTo(new[] { 12345, 0, 0, DecFlagsScale2 }));
            Assert.That(decimal.GetBits(-0.000001m), Is.EqualTo(new[] { 1, 0, 0, DecFlagsNegScale6 }));
            Assert.That(decimal.GetBits(4294967296m), Is.EqualTo(new[] { 0, 1, 0, 0 }), "2^32 -> mid");
            Assert.That(decimal.GetBits(18446744073709551616m), Is.EqualTo(new[] { 0, 0, 1, 0 }), "2^64 -> hi");
            Assert.That(decimal.GetBits(decimal.MaxValue), Is.EqualTo(new[] { -1, -1, -1, 0 }));
        });

        Assert.Multiple(() =>
        {
            // Wire -> managed. 2^32 vs 2^64 is the lo/mid/hi position oracle: a swapped pair
            // yields 1m or a wildly different magnitude, never the pinned value.
            Assert.That(Call("DecimalFromWire", 12345, 0, 0, DecFlagsScale2), Is.EqualTo(123.45m));
            Assert.That(Call("DecimalFromWire", 1, 0, 0, DecFlagsNegScale6), Is.EqualTo(-0.000001m));
            Assert.That(Call("DecimalFromWire", 0, 1, 0, 0), Is.EqualTo(4294967296m));
            Assert.That(Call("DecimalFromWire", 0, 0, 1, 0), Is.EqualTo(18446744073709551616m));
            Assert.That(Call("DecimalFromWire", -1, -1, -1, 0), Is.EqualTo(decimal.MaxValue));

            // Managed -> wire is GetBits verbatim — including .NET's preserved -0 sign flag,
            // which the NATIVE receiving side canonicalizes (the documented P1 divergence).
            Assert.That(Call("DecimalToWire", 123.45m), Is.EqualTo(new[] { 12345, 0, 0, DecFlagsScale2 }));
            Assert.That(Call("DecimalToWire", -0.000001m), Is.EqualTo(new[] { 1, 0, 0, DecFlagsNegScale6 }));
            Assert.That(Call("DecimalToWire", new decimal(0, 0, 0, true, 0)),
                Is.EqualTo(new[] { 0, 0, 0, DecFlagsNegZero }));
        });

        // Garbage flag bits (outside sign|scale) must throw, mirroring new decimal(int[]).
        Assert.Throws<ArgumentException>(() => Call("DecimalFromWire", 1, 0, 0, 0x00020001));
        Assert.Throws<ArgumentException>(() => Call("DecimalFromWire", 1, 0, 0, 0x001D0000)); // scale 29
    }

    [Test]
    public void Managed_Guid_WireVectors()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Guid.Parse(GuidNominal).ToByteArray(), Is.EqualTo(GuidNominalWire),
                "stale vector: the wire IS .NET's ToByteArray (mixed-endian)");
            Assert.That(Guid.Parse(GuidHighBit).ToByteArray(), Is.EqualTo(GuidHighBitWire),
                "stale vector: high-bit bytes");
        });

        Assert.Multiple(() =>
        {
            Assert.That(Call("GuidFromWire", GuidNominalWire), Is.EqualTo(Guid.Parse(GuidNominal)));
            Assert.That(Call("GuidToWire", Guid.Parse(GuidNominal)), Is.EqualTo(GuidNominalWire));
            // High-bit bytes in a/b/c catch sign-extension and byte-order mistakes.
            Assert.That(Call("GuidFromWire", GuidHighBitWire), Is.EqualTo(Guid.Parse(GuidHighBit)));
            Assert.That(Call("GuidToWire", Guid.Parse(GuidHighBit)), Is.EqualTo(GuidHighBitWire));
        });
    }

    [Test]
    public void Managed_DateTimeOffset_WireVectors()
    {
        var plus2 = new DateTimeOffset(2026, 8, 2, 10, 30, 0, TimeSpan.FromHours(2));
        var minus530 = new DateTimeOffset(2026, 8, 2, 10, 30, 0, new TimeSpan(-5, -30, 0));
        Assert.Multiple(() =>
        {
            Assert.That((plus2.Ticks, plus2.UtcTicks),
                Is.EqualTo((DtoClockTicks, DtoUtcTicksPlus2h)), "stale vector: +02:00");
            Assert.That((minus530.Ticks, minus530.UtcTicks),
                Is.EqualTo((DtoClockTicks, DtoUtcTicksMinus530)), "stale vector: -05:30");
        });

        // Wire -> managed: the wire carries the UTC instant; .NET's ctor wants CLOCK ticks, so
        // a reconstruction that forgets to add the offset back lands 2h/5.5h off.
        var fromPlus2 = (DateTimeOffset)Call("DateTimeOffsetFromWire", DtoUtcTicksPlus2h, DtoOffsetPlus2h)!;
        var fromMinus530 = (DateTimeOffset)Call("DateTimeOffsetFromWire", DtoUtcTicksMinus530, DtoOffsetMinus530)!;
        Assert.Multiple(() =>
        {
            Assert.That(fromPlus2, Is.EqualTo(plus2));
            Assert.That((fromPlus2.Ticks, fromPlus2.Offset.TotalMinutes), Is.EqualTo((DtoClockTicks, 120.0)));
            Assert.That(fromMinus530, Is.EqualTo(minus530));
            Assert.That((fromMinus530.Ticks, fromMinus530.Offset.TotalMinutes), Is.EqualTo((DtoClockTicks, -330.0)));
        });

        // Managed -> wire (out params read back through the invocation array).
        var mi = RequireHelper("DateTimeOffsetToWire");
        var argsPlus2 = new object?[] { plus2, null, null };
        mi.Invoke(null, argsPlus2);
        var argsMinus530 = new object?[] { minus530, null, null };
        mi.Invoke(null, argsMinus530);
        Assert.Multiple(() =>
        {
            Assert.That(argsPlus2[1], Is.EqualTo(DtoUtcTicksPlus2h));
            Assert.That(argsPlus2[2], Is.EqualTo(DtoOffsetPlus2h));
            Assert.That(argsMinus530[1], Is.EqualTo(DtoUtcTicksMinus530));
            Assert.That(argsMinus530[2], Is.EqualTo(DtoOffsetMinus530));
        });

        // Out-of-range offsets refuse (via .NET's own ctor validation).
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Call("DateTimeOffsetFromWire", DtoUtcTicksPlus2h, (short)841));
    }

    [Test]
    public void Managed_StringBuilder_OneDirectionOnly()
    {
        // §6.4: StringBuilder crosses BY VALUE as String, native -> managed only.
        var sb = Call("StringBuilderFromWire", "héllo");
        Assert.Multiple(() =>
        {
            Assert.That(sb, Is.InstanceOf<System.Text.StringBuilder>());
            Assert.That(sb!.ToString(), Is.EqualTo("héllo"));
            Assert.That(Call("StringBuilderFromWire", new object?[] { null }), Is.Null,
                "a null wire string is Nothing, not an empty StringBuilder");
            Assert.That(_marshal.GetMethod("StringBuilderToWire"), Is.Null,
                "§6.4 defines ONE direction for StringBuilder; a managed->wire helper would "
                + "invent a conversion the spec's table does not have.");
        });
    }

    /// <summary>
    /// The native mirror of the reflection pin above: §6.4's StringBuilder row has exactly one
    /// direction, so the native header must not grow a <c>from_net_stringbuilder</c> either.
    /// A content pin (not a compile) — the header's comments describe the absence without
    /// spelling the identifier, precisely so this assert stays meaningful.
    /// </summary>
    [Test]
    public void NativeHeader_HasNoStringBuilderFromNetDirection() =>
        Assert.That(CppNetMarshal.GuardedSource, Does.Not.Contain("from_net_stringbuilder"),
            "§6.4 defines ONE direction for StringBuilder — a native from_net_* converter "
            + "would invent the reverse conversion the spec's table does not have.");

    // ======================================================================================
    // FAST: wiring — the native header is spliced, surface-keyed, verbatim.
    // ======================================================================================

    [Test]
    public void MarshalHeaderIsCarriedSurfaceKeyedAndVerbatim()
    {
        var files = NetProxyEmitter.Emit(NetProxyEmitterTests.WireShapeSurface(), NetProxyEmitterTests.Module);

        Assert.Multiple(() =>
        {
            Assert.That(files.TryGetValue("blnet_marshal.hpp", out var text), Is.True,
                "blnet_marshal.hpp is missing from the §9.1 artifact set — Task 7a's lowered "
                + "calls have nothing to include.");
            Assert.That(text, Is.EqualTo(CppNetMarshal.GuardedSource),
                "blnet_marshal.hpp must be CppNetMarshal.GuardedSource verbatim — one source "
                + "of truth for the native bit layouts.");
            // The splice-home decision: surface-keyed, NEVER the always-emitted preamble.
            // (Empty-surface inertness itself is NetProxyEmitterTests' assertion.)
            Assert.That(NetProxyEmitter.Emit(BasicLang.Net.NetSurface.Empty, NetProxyEmitterTests.Module),
                Is.Empty);
        });
    }

    // ======================================================================================
    // INTEGRATION: native side — hex/bit vectors through a real C++ compiler.
    // ======================================================================================

    private (string exe, string argsTemplate)? _compiler;

    [OneTimeSetUp]
    public void FindCompiler() => _compiler = Native.CppCompile.FindRunCompiler();

    /// <summary>
    /// Compiles the P1 headers + <c>blnet_marshal.hpp</c> (in the documented include order) +
    /// <paramref name="mainBody"/> and returns stdout.
    /// </summary>
    private string Run(string mainBody)
    {
        if (_compiler is null) Assert.Ignore("No C++ compiler available");
        var src = "#include \"bl_bcltypes.hpp\"\n#include \"bl_decimal.hpp\"\n"
                + "#include \"blnet_marshal.hpp\"\n#include <cstdio>\n#include <cstring>\n"
                + mainBody;
        return Native.CppCompile.CompileAndRun(src, _compiler!.Value, new Dictionary<string, string>
        {
            ["bl_bcltypes.hpp"] = CppBclRuntime.BclHeader,
            ["bl_decimal.hpp"] = CppDecimalRuntime.DecimalHeader,
            ["blnet_marshal.hpp"] = CppNetMarshal.GuardedSource,
        });
    }

    private static void AssertMarkers(string output, params string[] markers)
    {
        foreach (var m in markers)
            Assert.That(output, Does.Contain(m + "=1"), $"marker '{m}' not 1; full output:\n{output}");
    }

    [Test, Category("Integration")]
    public void Native_DateTime_WireVectors()
    {
        var output = Run(@"
using namespace BasicLang;
int main() {
    /* to_net: the P1 dateData IS the wire — 2026-08-02T00:00:00, all three Kinds */
    printf(""dd_utc=%d\n"", net::to_net_datetime(DateTime::FromTicksAndKind(639212256000000000LL, DateTime::KindUtc)) == 0x48DEF028FEF4C000ULL);
    printf(""dd_unspec=%d\n"", net::to_net_datetime(DateTime::FromTicksAndKind(639212256000000000LL, DateTime::KindUnspecified)) == 0x08DEF028FEF4C000ULL);
    printf(""dd_local=%d\n"", net::to_net_datetime(DateTime::FromTicksAndKind(639212256000000000LL, DateTime::KindLocal)) == 0x88DEF028FEF4C000ULL);
    printf(""dd_max_local=%d\n"", net::to_net_datetime(DateTime::FromTicksAndKind(3155378975999999999LL, DateTime::KindLocal)) == 0xABCA2875F4373FFFULL);
    /* from_net: exact bit positions back out */
    DateTime lo = net::from_net_datetime(0x88DEF028FEF4C000ULL);
    printf(""fn_local=%d\n"", lo.Ticks() == 639212256000000000LL && lo.Kind() == DateTime::KindLocal);
    DateTime ut = net::from_net_datetime(0x48DEF028FEF4C000ULL);
    printf(""fn_utc=%d\n"", ut.Ticks() == 639212256000000000LL && ut.Kind() == DateTime::KindUtc);
    printf(""fn_zero=%d\n"", net::from_net_datetime(0ULL).Ticks() == 0 && net::from_net_datetime(0ULL).Kind() == DateTime::KindUnspecified);
    /* kind bits 3 never exist in a live .NET DateTime — must refuse, not fabricate */
    int threw = 0;
    try { (void)net::from_net_datetime((3ULL << 62) | 1ULL); } catch (const std::runtime_error&) { threw = 1; }
    printf(""fn_kind3_throws=%d\n"", threw);
    return 0;
}
");
        AssertMarkers(output,
            "dd_utc", "dd_unspec", "dd_local", "dd_max_local",
            "fn_local", "fn_utc", "fn_zero", "fn_kind3_throws");
    }

    [Test, Category("Integration")]
    public void Native_TimeSpan_WireVectors()
    {
        var output = Run(@"
using namespace BasicLang;
int main() {
    printf(""ts_nominal=%d\n"", net::to_net_timespan(TimeSpan(1, 2, 3, 4) + TimeSpan::FromTicks(5000000)) == 937845000000LL);
    printf(""ts_neg=%d\n"", net::to_net_timespan(TimeSpan::FromTicks(-1)) == -1LL);
    printf(""ts_min=%d\n"", net::to_net_timespan(TimeSpan::MinValue()) == INT64_MIN);
    printf(""fn_nominal=%d\n"", net::from_net_timespan(937845000000LL).Ticks() == 937845000000LL);
    printf(""fn_min=%d\n"", net::from_net_timespan(INT64_MIN).Ticks() == INT64_MIN);
    return 0;
}
");
        AssertMarkers(output, "ts_nominal", "ts_neg", "ts_min", "fn_nominal", "fn_min");
    }

    [Test, Category("Integration")]
    public void Native_Decimal_WireVectors()
    {
        var output = Run(@"
using namespace BasicLang;
int main() {
    /* to_net: {lo, mid, hi, flags} verbatim; 2^32 / 2^64 pin the limb POSITIONS */
    auto nom = net::to_net_decimal(Decimal::Parse(""123.45""));
    printf(""dec_nominal=%d\n"", nom.lo == 12345u && nom.mid == 0u && nom.hi == 0u && nom.flags == 0x00020000u);
    auto neg = net::to_net_decimal(Decimal::Parse(""-0.000001""));
    printf(""dec_neg_scale6=%d\n"", neg.lo == 1u && neg.mid == 0u && neg.hi == 0u && neg.flags == 0x80060000u);
    auto p32 = net::to_net_decimal(Decimal::Parse(""4294967296""));
    printf(""dec_2pow32_mid=%d\n"", p32.lo == 0u && p32.mid == 1u && p32.hi == 0u && p32.flags == 0u);
    auto p64 = net::to_net_decimal(Decimal::Parse(""18446744073709551616""));
    printf(""dec_2pow64_hi=%d\n"", p64.lo == 0u && p64.mid == 0u && p64.hi == 1u && p64.flags == 0u);
    auto mx = net::to_net_decimal(Decimal::MaxValue());
    printf(""dec_max=%d\n"", mx.lo == 0xFFFFFFFFu && mx.mid == 0xFFFFFFFFu && mx.hi == 0xFFFFFFFFu && mx.flags == 0u);
    /* from_net: value semantics land exactly (ToString is scale-preserving) */
    printf(""fn_nominal=%d\n"", net::from_net_decimal(12345u, 0u, 0u, 0x00020000u).ToString() == std::string(""123.45""));
    printf(""fn_neg=%d\n"", net::from_net_decimal(1u, 0u, 0u, 0x80060000u).ToString() == std::string(""-0.000001""));
    printf(""fn_2pow32=%d\n"", net::from_net_decimal(0u, 1u, 0u, 0u).ToString() == std::string(""4294967296""));
    /* the DOCUMENTED P1 divergence: .NET -0 bits canonicalize to +0 on the native side */
    auto nz = net::from_net_decimal(0u, 0u, 0u, 0x80000000u);
    printf(""fn_negzero_canonical=%d\n"", nz.flags_ == 0u && nz.IsZeroMag());
    /* garbage flag bits / scale 29 refuse, mirroring new decimal(int[]) */
    int threw = 0;
    try { (void)net::from_net_decimal(1u, 0u, 0u, 0x00020001u); } catch (const std::runtime_error&) { threw = 1; }
    printf(""fn_badflags_throws=%d\n"", threw);
    threw = 0;
    try { (void)net::from_net_decimal(1u, 0u, 0u, 0x001D0000u); } catch (const std::runtime_error&) { threw = 1; }
    printf(""fn_scale29_throws=%d\n"", threw);
    return 0;
}
");
        AssertMarkers(output,
            "dec_nominal", "dec_neg_scale6", "dec_2pow32_mid", "dec_2pow64_hi", "dec_max",
            "fn_nominal", "fn_neg", "fn_2pow32", "fn_negzero_canonical",
            "fn_badflags_throws", "fn_scale29_throws");
    }

    [Test, Category("Integration")]
    public void Native_Guid_WireVectors()
    {
        var output = Run(@"
using namespace BasicLang;
int main() {
    const uint8_t nominal[16] = { 0x33, 0x22, 0x11, 0x00, 0x55, 0x44, 0x77, 0x66,
                                  0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
    const uint8_t highbit[16] = { 0xCC, 0xDD, 0xEE, 0xFF, 0xAA, 0xBB, 0x88, 0x99,
                                  0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, 0x00 };
    Guid g1 = Guid::Parse(""00112233-4455-6677-8899-aabbccddeeff"");
    uint8_t w[16];
    net::to_net_guid(g1, w);
    printf(""guid_to_wire=%d\n"", std::memcmp(w, nominal, 16) == 0);
    printf(""guid_from_wire=%d\n"", net::from_net_guid(nominal) == g1);
    /* high-bit bytes in a/b/c catch sign-extension and byte-order mistakes */
    Guid g2 = Guid::Parse(""ffeeddcc-bbaa-9988-7766-554433221100"");
    net::to_net_guid(g2, w);
    printf(""guid_hb_to_wire=%d\n"", std::memcmp(w, highbit, 16) == 0);
    Guid r2 = net::from_net_guid(highbit);
    printf(""guid_hb_from_wire=%d\n"", r2 == g2);
    printf(""guid_hb_string=%d\n"", r2.ToString() == std::string(""ffeeddcc-bbaa-9988-7766-554433221100""));
    return 0;
}
");
        AssertMarkers(output,
            "guid_to_wire", "guid_from_wire", "guid_hb_to_wire", "guid_hb_from_wire", "guid_hb_string");
    }

    [Test, Category("Integration")]
    public void Native_DateTimeOffset_WireVectors()
    {
        var output = Run(@"
using namespace BasicLang;
int main() {
    /* clock 2026-08-02T10:30:00 at +02:00 -> UTC instant 639212562000000000 */
    DateTimeOffset plus2(DateTime::FromTicksAndKind(639212634000000000LL, DateTime::KindUnspecified),
                         TimeSpan(120LL * TimeSpan::TicksPerMinute));
    auto w1 = net::to_net_datetimeoffset(plus2);
    printf(""dto_plus2=%d\n"", w1.utcTicks == 639212562000000000LL && w1.offsetMinutes == 120);
    /* same clock at -05:30 -> UTC instant 639212832000000000 */
    DateTimeOffset minus530(DateTime::FromTicksAndKind(639212634000000000LL, DateTime::KindUnspecified),
                            TimeSpan(-330LL * TimeSpan::TicksPerMinute));
    auto w2 = net::to_net_datetimeoffset(minus530);
    printf(""dto_minus530=%d\n"", w2.utcTicks == 639212832000000000LL && w2.offsetMinutes == -330);
    /* from_net: the UTC instant is stored; the clock is utc + offset */
    DateTimeOffset r1 = net::from_net_datetimeoffset(639212562000000000LL, 120);
    printf(""fn_plus2=%d\n"", r1.ClockDateTime().Ticks() == 639212634000000000LL && r1.offsetMinutes_ == 120);
    DateTimeOffset r2 = net::from_net_datetimeoffset(639212832000000000LL, -330);
    printf(""fn_minus530=%d\n"", r2.ClockDateTime().Ticks() == 639212634000000000LL && r2.offsetMinutes_ == -330);
    printf(""fn_equal=%d\n"", r1 == plus2 && r2 == minus530);
    /* offsets beyond +/-14:00 refuse */
    int threw = 0;
    try { (void)net::from_net_datetimeoffset(639212562000000000LL, 841); } catch (const std::runtime_error&) { threw = 1; }
    printf(""fn_offset_range_throws=%d\n"", threw);
    return 0;
}
");
        AssertMarkers(output,
            "dto_plus2", "dto_minus530", "fn_plus2", "fn_minus530", "fn_equal",
            "fn_offset_range_throws");
    }

    [Test, Category("Integration")]
    public void Native_StringBuilder_CrossesByValueAsString()
    {
        var output = Run(@"
using namespace BasicLang;
int main() {
    /* one direction only (§6.4): the value that crosses is the UTF-8 CONTENT */
    auto sb = std::make_shared<StringBuilder>(std::string(""h\xC3\xA9llo""));
    std::string wire = net::to_net_stringbuilder(*sb);
    printf(""sb_value=%d\n"", wire == std::string(""h\xC3\xA9llo""));
    printf(""sb_bytes=%d\n"", wire.size() == 6 && (unsigned char)wire[1] == 0xC3u && (unsigned char)wire[2] == 0xA9u);
    auto empty = std::make_shared<StringBuilder>();
    printf(""sb_empty=%d\n"", net::to_net_stringbuilder(*empty) == std::string(""""));
    return 0;
}
");
        AssertMarkers(output, "sb_value", "sb_bytes", "sb_empty");
    }
}
