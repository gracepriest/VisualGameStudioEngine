using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace BasicLang.Compiler.CodeGen.Net
{
    /// <summary>
    /// Single source of truth for the GENERATED shim's fixed C# scaffolding — the part of a
    /// shim that is identical for every project, independent of which .NET types it bridges
    /// (spec: docs/superpowers/specs/2026-07-26-dotnet-native-boundary-contract-design.md §8.1).
    /// The per-project part (<c>Exports.g.cs</c>, the proxies) is emitted by NetShimGenerator.
    /// <para/>
    /// <b>Why this class exists.</b> P0 hand-wrote a shim at
    /// <c>VisualGameStudio.Tests/TestAssets/BlnetTestShim/</c> and froze a 16-scenario
    /// conformance suite against it (spec §12.2). That suite only proves anything about
    /// GENERATED shims if the generated handle model is the same handle model. The three
    /// §12.4 drift invariants in <c>BlnetShimSourcesTests</c> tie the two together:
    /// <list type="number">
    /// <item><see cref="HandleTable"/> is byte-equal (modulo CRLF) to the hand-written
    /// <c>HandleTable.cs</c> the frozen suite exercises — update BOTH, or neither.</item>
    /// <item><see cref="BlnetStatusCs"/> is generated from <see cref="BlnetContract"/>,
    /// never hand-copied.</item>
    /// <item><see cref="ShimAbiCs"/> interpolates <see cref="BlnetContract.AbiVersion"/>.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <b>NAMESPACE DECISION — binding on NetShimGenerator (plan Task 14).</b>
    /// The generated shim keeps the hand shim's namespace, <c>BlnetTestShim</c>, verbatim.
    /// The name is arbitrary and never crosses the C ABI (only the
    /// <c>UnmanagedCallersOnly(EntryPoint = ...)</c> strings do), and keeping it is what lets
    /// invariant (1) be a byte-equality assert instead of a weaker compare-modulo-the-
    /// namespace-line. Consequences a generator must honour:
    /// <list type="bullet">
    /// <item><c>Exports.g.cs</c> MUST declare <c>namespace BlnetTestShim;</c> so it can see
    /// <c>HandleTable</c>.</item>
    /// <item><see cref="BlnetStatusCs"/> carries NO namespace, because invariant (2) pins it
    /// byte-for-byte to <see cref="BlnetContract.GenerateStatusEnumCs"/>, which emits none.
    /// <c>BlnetStatus</c> therefore lands in the GLOBAL namespace in a generated shim (it is
    /// inside <c>BlnetTestShim</c> in the hand shim). This compiles: name lookup from inside
    /// <c>BlnetTestShim</c> falls out to the global namespace. Do not "fix" it by prepending a
    /// namespace line — that breaks invariant (2). This is the one shape divergence between
    /// the hand and generated shims, and it is semantically inert.</item>
    /// <item>The emitted <c>.csproj</c> MUST set <c>ImplicitUsings=enable</c>:
    /// <see cref="HandleTable"/> relies on it for <c>System.Collections.Generic</c>
    /// (<c>List&lt;T&gt;</c>, <c>Stack&lt;T&gt;</c>) and <c>System.Linq</c>
    /// (<c>Count(predicate)</c> in <c>AliveCount</c>).</item>
    /// </list>
    /// </remarks>
    public static class BlnetShimSources
    {
        /// <summary>
        /// Complete <c>HandleTable.g.cs</c> text (spec C2: generation-tagged GCHandle table).
        /// Byte-equal to the hand-written copy the frozen P0 conformance suite validates —
        /// pinned by <c>BlnetShimSourcesTests</c>.
        /// </summary>
        public static string HandleTable => HandleTableCs;

        /// <summary>
        /// Complete <c>BlnetStatus.g.cs</c> text, generated from the contract table at read
        /// time so it can never drift. Carries no namespace — see the namespace decision above.
        /// </summary>
        public static string BlnetStatusCs => BlnetContract.GenerateStatusEnumCs();

        /// <summary>
        /// Complete <c>ShimAbi.g.cs</c> text, with the ABI constant spliced from
        /// <see cref="BlnetContract.AbiVersion"/> at read time. This is a SEPARATE file, not an
        /// appendix to the status enum: the hand shim appends <c>ShimAbi</c> to
        /// <c>BlnetStatus.cs</c>, but a generated shim cannot, because
        /// <see cref="BlnetStatusCs"/> is pinned byte-for-byte to
        /// <see cref="BlnetContract.GenerateStatusEnumCs"/>.
        /// </summary>
        public static string ShimAbiCs => ShimAbiPrefix + BlnetContract.AbiVersion + ShimAbiSuffix;

        /// <summary><c>ShimAbi.g.cs</c> — everything before the spliced ABI version.</summary>
        private const string ShimAbiPrefix = @"// GENERATED from BlnetContract.AbiVersion — do not edit by hand.
namespace BlnetTestShim;

public static class ShimAbi { public const int AbiVersion = ";

        /// <summary><c>ShimAbi.g.cs</c> — everything after the spliced ABI version.</summary>
        private const string ShimAbiSuffix = @"; }
";

        /// <summary>
        /// Complete <c>BlnetMarshal.cs</c> text — spec §6.4's conversion pairs, MANAGED side
        /// (P2a-2 Task 6). Static helpers the generated <c>Exports.g.cs</c> wrappers call (from
        /// Task 7a on) to reconstruct the six P1 <c>NativeOwned</c> value types from their wire
        /// forms and to flatten results back. The wire forms are the P1 native layouts VERBATIM
        /// (see <c>CppNetMarshal</c>, the native half); the bit positions on the two sides are
        /// pinned against the same hard-coded .NET-computed vectors by
        /// <c>NetConversionPairTests</c>, because a disagreement is a silently wrong value.
        /// <para/>
        /// Lives in <see cref="BlnetShimSources"/> rather than being generator-emitted so
        /// <c>NetShimCache.TemplateIdentity</c> hashes it as fixed template text — editing a
        /// conversion invalidates every cached shim, which is correct since the shim content
        /// changes. Unlike the three files above it is NOT byte-pinned to a hand-shim copy
        /// (the hand shim predates the conversion pairs), so it may evolve freely.
        /// </summary>
        public static string MarshalCs => MarshalCsText;

        private const string MarshalCsText = @"// BlnetMarshal.cs — GENERATED (spliced from BasicLang BlnetShimSources.MarshalCs).
// Do not edit; edits are lost on the next build. Spec §6.4: conversion pairs for the
// six P1 NativeOwned value types. Wire forms are the P1 native layouts verbatim; the
// native mirror is blnet_marshal.hpp (CppNetMarshal.cs), and NetConversionPairTests
// pins both sides to the same .NET-computed bit vectors.
//
// Direction convention (the shim's point of view): *FromWire reconstructs the real
// .NET value from what the native side sent; *ToWire flattens a .NET result for the
// trip back. Explicit usings on purpose — this file is also compiled by raw Roslyn
// in tests, where MSBuild's ImplicitUsings do not exist.
#nullable enable
using System;
using System.Text;

namespace BlnetTestShim;

public static class BlnetMarshal
{
    // ---- DateTime <-> uint64 dateData: ticks in the low 62 bits | kind in the top 2
    // (DateTimeKind's own numbering). Equals DateTime.ToBinary() for Utc/Unspecified;
    // for Local the wire is the RAW pair — ToBinary's UTC adjustment is
    // serialization-only, which is why this uses the (long, DateTimeKind) constructor
    // and NEVER DateTime.FromBinary (FromBinary would shift Local ticks by the zone).
    private const ulong DateTimeTicksMask = 0x3FFFFFFFFFFFFFFFUL;

    public static DateTime DateTimeFromWire(ulong dateData) =>
        // The ctor rejects kind values outside Unspecified/Utc/Local (top bits 3).
        new DateTime((long)(dateData & DateTimeTicksMask), (DateTimeKind)(dateData >> 62));

    public static ulong DateTimeToWire(DateTime value) =>
        (ulong)value.Ticks | ((ulong)(uint)value.Kind << 62);

    // ---- TimeSpan <-> int64 ticks. ----
    public static TimeSpan TimeSpanFromWire(long ticks) => new TimeSpan(ticks);

    public static long TimeSpanToWire(TimeSpan value) => value.Ticks;

    // ---- Decimal <-> the decimal.GetBits quad {lo, mid, hi, flags}: 96-bit
    // magnitude, flags = scale (0-28) in bits 16-23 | sign in bit 31. The int[]
    // constructor validates the flag bits exactly like the native from_net_decimal.
    // ToWire is GetBits VERBATIM — a .NET negative zero keeps its sign flag here and
    // is canonicalized to +0 on the native side (the documented P1 divergence). ----
    public static decimal DecimalFromWire(int lo, int mid, int hi, int flags) =>
        new decimal(new[] { lo, mid, hi, flags });

    public static int[] DecimalToWire(decimal value) => decimal.GetBits(value);

    // ---- Guid <-> the 16 Guid.ToByteArray() bytes (a, b, c little-endian, then the
    // trailing 8 verbatim — .NET's mixed-endian order, which the byte[] constructor
    // reverses exactly). ----
    public static Guid GuidFromWire(byte[] bytes) => new Guid(bytes);

    public static byte[] GuidToWire(Guid value) => value.ToByteArray();

    // ---- DateTimeOffset <-> {utcTicks, offsetMinutes}: the P1 native fields
    // verbatim. .NET's constructor takes CLOCK ticks, so reconstruction adds the
    // offset back onto the UTC instant; the constructor then validates both the
    // offset (+/-14:00) and the resulting instant. ----
    public static DateTimeOffset DateTimeOffsetFromWire(long utcTicks, short offsetMinutes) =>
        new DateTimeOffset(
            utcTicks + offsetMinutes * TimeSpan.TicksPerMinute,
            new TimeSpan(offsetMinutes * TimeSpan.TicksPerMinute));

    public static void DateTimeOffsetToWire(
        DateTimeOffset value, out long utcTicks, out short offsetMinutes)
    {
        utcTicks = value.UtcTicks;
        offsetMinutes = (short)(value.Offset.Ticks / TimeSpan.TicksPerMinute);
    }

    // ---- StringBuilder: crosses BY VALUE as String, ONE direction only (§6.4's
    // table) — there is deliberately no StringBuilderToWire. A null wire string is
    // Nothing and stays null (never an empty StringBuilder). ----
    public static StringBuilder? StringBuilderFromWire(string? value) =>
        value is null ? null : new StringBuilder(value);
}
";

        /// <summary>
        /// <c>HandleTable.g.cs</c> verbatim. MUST stay byte-identical to
        /// <c>VisualGameStudio.Tests/TestAssets/BlnetTestShim/HandleTable.cs</c>.
        /// </summary>
        private const string HandleTableCs = @"using System.Runtime.InteropServices;

namespace BlnetTestShim;

/// <summary>
/// Spec C2: generation-tagged table of GCHandles. Handle = {generation:high32 | index:low32}.
/// Index 0 reserved. Fresh handle refcount = 1. Generation increments when a slot is FREED
/// (refcount hits zero), not per release. Table grows without bound (amortized append).
/// </summary>
public sealed class HandleTable
{
    private struct Slot { public GCHandle Gc; public uint Generation; public int RefCount; public bool Alive; }
    private readonly object _lock = new();
    private readonly List<Slot> _slots = new() { default };  // burn index 0
    private readonly Stack<uint> _free = new();

    public ulong Create(object target)
    {
        lock (_lock)
        {
            uint index;
            if (_free.Count > 0) index = _free.Pop();
            else { _slots.Add(default); index = (uint)(_slots.Count - 1); }
            var s = _slots[(int)index];
            if (s.Generation == 0) s.Generation = 1;
            s.Gc = GCHandle.Alloc(target);
            s.RefCount = 1;
            s.Alive = true;
            _slots[(int)index] = s;
            return ((ulong)s.Generation << 32) | index;
        }
    }

    public BlnetStatus TryGet(ulong handle, out object? target)
    {
        lock (_lock)
        {
            target = null;
            if (!Validate(handle, out var index)) return BlnetStatus.BLNET_E_STALE_HANDLE;
            target = _slots[(int)index].Gc.Target;
            return BlnetStatus.BLNET_OK;
        }
    }

    public BlnetStatus AddRef(ulong handle)
    {
        lock (_lock)
        {
            if (!Validate(handle, out var index)) return BlnetStatus.BLNET_E_STALE_HANDLE;
            var s = _slots[(int)index]; s.RefCount++; _slots[(int)index] = s;
            return BlnetStatus.BLNET_OK;
        }
    }

    public BlnetStatus Release(ulong handle)
    {
        lock (_lock)
        {
            if (!Validate(handle, out var index)) return BlnetStatus.BLNET_E_STALE_HANDLE;
            var s = _slots[(int)index];
            if (--s.RefCount == 0)
            {
                s.Gc.Free();
                s.Alive = false;
                s.Generation++;          // stale detection: old handles now fail Validate
                _slots[(int)index] = s;
                _free.Push(index);
            }
            else _slots[(int)index] = s;
            return BlnetStatus.BLNET_OK;
        }
    }

    public int AliveCount { get { lock (_lock) { return _slots.Count(s => s.Alive); } } }

    private bool Validate(ulong handle, out uint index)
    {
        index = (uint)(handle & 0xFFFFFFFF);
        uint gen = (uint)(handle >> 32);
        if (index == 0 || index >= _slots.Count) return false;
        var s = _slots[(int)index];
        return s.Alive && s.Generation == gen;
    }
}
";
    }
}
