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
