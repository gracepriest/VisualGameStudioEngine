using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicLang.Net
{
    /// <summary>
    /// One delegate type the surface needs a §8.4 dispatcher for.
    /// </summary>
    /// <param name="DelegateFullName">
    /// The delegate TYPE, fully qualified — <c>System.Text.RegularExpressions.MatchEvaluator</c>.
    /// </param>
    /// <param name="InvokeSignature">
    /// Its <c>Invoke</c> signature as <c>Return(Param,Param)</c>, carried onto the surface by
    /// <c>NetTypeResolver.Describe</c> (decision D-P9). Neither emitter can re-derive it.
    /// </param>
    /// <param name="HelperName">
    /// The dispatcher's MANAGED method name inside <c>Exports.g.cs</c>.
    ///
    /// <para>⛔ <b>Not an export, and deliberately not called one.</b> §8.4's dispatcher "wraps a
    /// callback handle in a real .NET delegate of the required type and invokes the universal
    /// thunk" (spec §8.4) — it is invoked from inside a member wrapper on the MANAGED side.
    /// Nothing native ever calls it: the native side mints its handle through
    /// <c>blnet_register_callback</c>, which lives in <c>blnet_runtime.hpp</c> and is not a shim
    /// export at all. So the dispatcher has no <c>BlnetProxyTable</c> slot and is not an
    /// <c>[UnmanagedCallersOnly]</c> entry.</para>
    ///
    /// <para>That is why §12.4 (slots ≡ exports) simply does not RANGE over it — which is a
    /// different claim from the "§12.4-exempt" framing <c>NetShimGenerator</c>'s header rules
    /// out. An exemption would carve a hole in an invariant that covers the thing; here the
    /// invariant never covered it, and adding these names to the export set would BREAK it by
    /// making exports exceed slots. Pinned by
    /// <c>NetDelegateTests.ADelegateBearingSurface_KeepsSlotsAndExportsEqual</c>.</para>
    /// </param>
    internal sealed record NetDelegateForm(
        string DelegateFullName,
        string InvokeSignature,
        string HelperName);

    /// <summary>
    /// The single shared derivation of §8.4's delegate dispatchers — P2a-2 Task 11 Step 3,
    /// deliberately shaped after <see cref="NetArrayCopy"/>'s <c>RequiredForms</c>.
    ///
    /// <para><b>Why one function.</b> The delegate set is derived from the surface in more than
    /// one place — the shim generator emits a dispatcher per form, and the proxy emitter must
    /// agree with it about which parameters are callback-shaped. Deriving that set twice is how
    /// the two halves of a boundary drift apart, so it is derived once here.</para>
    ///
    /// <para><b>These are NOT exports, and §12.4 does not range over them.</b>
    /// <see cref="NetShimGenerator"/>'s header rules out answering "§12.4-exempt" for the
    /// dispatcher — correctly, but the answer is not an exemption either. Per spec §8.4 the
    /// dispatcher is a MANAGED helper invoked from inside a member wrapper; the native side mints
    /// its callback handle through <c>blnet_register_callback</c> in <c>blnet_runtime.hpp</c>,
    /// which is not a shim export. No native caller ⇒ no proxy slot ⇒ not an export. Adding these
    /// names to the §12.4 set would make exports exceed slots and break the very invariant the
    /// warning exists to protect.</para>
    ///
    /// <para><b>PARAMETERS only, never results.</b> Spec decision D6 scopes P2a to delegate
    /// ARGUMENTS; events and interface implementation are P2b+. A method that RETURNS a delegate
    /// is an outbound handle, not a dispatcher, and gives it no entry here.</para>
    ///
    /// <para><b>Ordinal order, not encounter order.</b> <see cref="NetArrayCopy"/> can use its
    /// fixed table's order because §8.6's admitted element set is CLOSED. The delegate set is
    /// open — it is whatever the program calls — so determinism has to come from sorting. The
    /// export set is part of §10.2's shim cache key, and an order that depended on IR walk order
    /// would produce false cache misses (a ~27 s republish for an unchanged surface).</para>
    /// </summary>
    /// <summary>How one §8.4 callback slot's 64-bit word is encoded. Mirrors the native
    /// <c>BlnetSlotKind</c> — the values are NOT parallel by accident, <see
    /// cref="NetDelegateDispatch.CppSlotDescriptors"/> renders these straight into it.</summary>
    internal enum NetSlotKind
    {
        /// <summary><c>BLNET_SLOT_VALUE</c> — a blittable scalar, in-slot.</summary>
        Value,

        /// <summary><c>BLNET_SLOT_STRING</c> — a UTF-8 <c>char*</c>, C3 ownership.</summary>
        String,

        /// <summary><c>BLNET_SLOT_HANDLE</c> — a <c>blnet_handle</c>, refcounted.</summary>
        Handle,
    }

    internal static class NetDelegateDispatch
    {
        /// <summary>
        /// <b>THE</b> §8.4 slot classification — which wire form a delegate's parameter or
        /// return type crosses as, and why it is refused when it does not.
        ///
        /// <para><b>Why it lives here rather than in either emitter.</b> Three places have to
        /// agree about a callback slot, and they are in three different languages:
        /// <c>NetShimGenerator</c> emits the managed pack/unpack, <c>CppCodeGenerator</c> emits
        /// the native adapter, and <see cref="CppSlotDescriptors"/> emits the
        /// <c>BlnetSlotDesc[]</c> the RUNTIME reads to decide what to deep-copy at enqueue.
        /// Disagreement between the first two is a compile error; disagreement with the THIRD is
        /// silent — a handle slot mislabelled <c>VALUE</c> skips the enqueue addref and the
        /// object can be collected before the pump runs it. So the classification is derived
        /// once, here, and the other three project from it.</para>
        ///
        /// <para><b>What stays refused, each for its own reason</b> (§8.4, resolved 2026-09-15 —
        /// and, exactly as §8.3 insists, no one of these may be widened by analogy with
        /// another):</para>
        /// <list type="bullet">
        /// <item><description><c>System.Object</c> — §8.3 rejects it PERMANENTLY, in every
        /// position, because <c>void*</c> erasure is unsound. It reaches this function as a
        /// type with no marshal row, which is the handle rule, so it needs its own arm ahead of
        /// that or the handle rule would quietly admit the one type §8.3 never will.</description></item>
        /// <item><description><c>Boolean</c> and <c>Char</c> — their WIRE spelling differs from
        /// their C++ spelling (<c>int32_t</c>/<c>uint16_t</c> against <c>bool</c>/<c>char16_t</c>),
        /// so the adapter's parameter type and the slot's type are not the same type. A
        /// pre-existing refusal, unrelated to the marshaling contract this resolves.</description></item>
        /// <item><description>§6.4 conversion rows and multi-slot pairs — a callback slot is
        /// ONE 64-bit word and these need a converted temporary or several words.</description></item>
        /// </list>
        /// </summary>
        internal static bool TryClassifySlot(
            string typeFullName, out NetSlotKind kind, out string refusal)
        {
            kind = default;
            refusal = null;

            // ⛔ MUST precede the no-row handle rule below. Object HAS no marshal row, so that
            // rule would admit it as a handle — and §8.3 rejects it permanently.
            if (typeFullName == "System.Object")
            {
                refusal = "'System.Object' is permanently unmarshalable (§8.3): it erases to "
                    + "void* and nothing on either side can recover what it was. Use the "
                    + "concrete type the callback actually receives.";
                return false;
            }

            if (!NetMarshalTable.TryGetWireRow(typeFullName, out var row))
            {
                // Every reference type with no special row crosses as an opaque handle — the
                // safe default §8.3 already relies on.
                kind = NetSlotKind.Handle;
                return true;
            }

            switch (row.Shape)
            {
                case NetWireShape.Scalar when !string.IsNullOrEmpty(row.CWire):
                    kind = NetSlotKind.Value;
                    return true;

                case NetWireShape.String:
                    kind = NetSlotKind.String;
                    return true;

                case NetWireShape.Boolean:
                case NetWireShape.Char:
                    refusal = $"'{typeFullName}' has a wire spelling ({row.CWire}) that differs "
                        + "from its C++ spelling, so the adapter's parameter and the slot are "
                        + "not the same type. Unrelated to §8.4's marshaling contract; use the "
                        + "wire-width integer instead.";
                    return false;

                default:
                    refusal = $"'{typeFullName}' is a §6.4 conversion or multi-slot row, and a "
                        + "callback slot is ONE 64-bit word. §8.4 admits blittable scalars, "
                        + "handles and strings; a converted temporary or a multi-word slot "
                        + "needs a contract §8.4 does not specify.";
                    return false;
            }
        }

        /// <summary>
        /// <see cref="TryClassifySlot"/>'s verdict for a slot already known to be admissible.
        /// Throws rather than returning a default, because a default here is the silent
        /// mislabelling <see cref="TryClassifySlot"/>'s remarks describe.
        /// </summary>
        internal static NetSlotKind SlotKind(string typeFullName) =>
            TryClassifySlot(typeFullName, out var kind, out var refusal)
                ? kind
                : throw new NotSupportedException(
                    "§8.4 slot classification asked for an inadmissible type: " + refusal);

        /// <summary>
        /// The delegate types <paramref name="surface"/> needs dispatchers for: every distinct
        /// delegate-typed PARAMETER across its collected members, ordinal by type name.
        /// </summary>
        internal static IReadOnlyList<NetDelegateForm> RequiredForms(NetSurface surface)
        {
            if (surface?.Members == null) return Array.Empty<NetDelegateForm>();

            // Keyed on the delegate TYPE, not its signature: System.Action and
            // System.Threading.ThreadStart are both `void()`, and the managed dispatcher has to
            // construct the right named delegate. SortedDictionary supplies the determinism.
            var byType = new SortedDictionary<string, string>(StringComparer.Ordinal);

            foreach (var member in surface.Members)
            {
                if (member?.Parameters == null) continue;
                foreach (var parameter in member.Parameters)
                {
                    if (parameter?.DelegateInvokeSignature == null) continue;
                    if (string.IsNullOrEmpty(parameter.TypeFullName)) continue;
                    byType[parameter.TypeFullName] = parameter.DelegateInvokeSignature;
                }
            }

            if (byType.Count == 0) return Array.Empty<NetDelegateForm>();

            return byType
                .Select(entry => new NetDelegateForm(
                    entry.Key, entry.Value, NetNameMangler.MangleDelegate(entry.Key)))
                .ToList();
        }

        /// <summary>
        /// <c>Return(Param,Param)</c> → its parts. THE single parser for an invoke signature:
        /// the shim generator needs it to emit a dispatcher, and the native side needs it to
        /// compute <c>BlnetSlotDesc[]</c>. Two copies would be two chances to disagree about
        /// arity, and arity is exactly what must not be wrong (see <see cref="SlotCount"/>).
        ///
        /// <para>Deliberately a bracket-aware split rather than a type parser: the string was
        /// RENDERED by <c>NetTypeResolver</c> from Roslyn symbols, so its shape is known, and a
        /// generic argument's commas are the only nesting that can occur. Splitting naively would
        /// report three parameters for <c>(List&lt;int,string&gt;, int)</c>.</para>
        /// </summary>
        internal static bool TryParseInvokeSignature(
            string signature, out string returnType, out IReadOnlyList<string> parameters)
        {
            returnType = null;
            parameters = Array.Empty<string>();
            if (string.IsNullOrEmpty(signature)) return false;

            var open = signature.IndexOf('(');
            if (open <= 0 || !signature.EndsWith(")", StringComparison.Ordinal)) return false;

            returnType = signature.Substring(0, open);
            var inner = signature.Substring(open + 1, signature.Length - open - 2);
            if (inner.Length == 0) return true;

            var parts = new List<string>();
            var depth = 0;
            var start = 0;
            for (var i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '<') depth++;
                else if (inner[i] == '>') depth--;
                else if (inner[i] == ',' && depth == 0)
                {
                    parts.Add(inner.Substring(start, i - start));
                    start = i + 1;
                }
            }
            parts.Add(inner.Substring(start));
            parameters = parts;
            return true;
        }

        /// <summary>
        /// How many argument slots a delegate's registration declares.
        ///
        /// <para>⛔ <b>This must equal the arity the delegate is INVOKED with.</b> P0's thunk
        /// deep-copies a queued invocation by indexing <c>snapshot.slots[i]</c> for
        /// <c>i</c> in <c>[0, argc)</c>, where <c>argc</c> comes from the INVOKE while
        /// <c>slots</c> holds only the registration-time entries — and there is no bounds check.
        /// Registering a different arity than the delegate is invoked with is an out-of-bounds
        /// read, not a mismatch error.</para>
        /// </summary>
        internal static int SlotCount(string invokeSignature) =>
            TryParseInvokeSignature(invokeSignature, out _, out var parameters)
                ? parameters.Count
                : 0;

        /// <summary>
        /// The C++ <c>BlnetSlotDesc[]</c> initializer for a delegate's parameters, or
        /// <c>null</c> when it takes none — <c>blnet_register_callback</c> guards that case
        /// explicitly (<c>if (argc &gt; 0) e.slots.assign(…)</c>), so a zero-arg registration
        /// may legally pass <c>nullptr</c> and allocating an empty array would be pretend-work.
        ///
        /// <para><b>The kind per slot is load-bearing, and its failure mode is silent.</b>
        /// This array is what <c>blnet_invoke_callback</c> reads when a callback is QUEUED
        /// rather than run inline: <c>BLNET_SLOT_HANDLE</c> makes it addref the handle at
        /// enqueue (the pump releases it after), <c>BLNET_SLOT_STRING</c> makes it deep-copy the
        /// buffer, and <c>BLNET_SLOT_VALUE</c> makes it do neither. Labelling a handle slot
        /// VALUE therefore compiles, links, and passes every inline test — and then lets the
        /// object be collected before the pump runs. That is why this renders
        /// <see cref="TryClassifySlot"/>'s verdict rather than a constant, and why
        /// <c>NetDelegateSlotWireTests</c> pins the rendering per kind.</para>
        ///
        /// <para><c>size</c> stays 0: it is documented as meaningful only for STRUCT and OUT,
        /// and §8.4 admits neither.</para>
        /// </summary>
        internal static string CppSlotDescriptors(string invokeSignature)
        {
            if (!TryParseInvokeSignature(invokeSignature, out _, out var parameters)
                || parameters.Count == 0)
            {
                return null;
            }

            var descriptors = parameters.Select(t => SlotKind(t) switch
            {
                NetSlotKind.Handle => "{BLNET_SLOT_HANDLE, 0}",
                NetSlotKind.String => "{BLNET_SLOT_STRING, 0}",
                _ => "{BLNET_SLOT_VALUE, 0}",
            });

            return "{ " + string.Join(", ", descriptors) + " }";
        }

        /// <summary>
        /// Every managed helper name <see cref="RequiredForms"/> implies, in the same order.
        /// ⛔ These are shim-internal method names, NOT exports — see
        /// <see cref="NetDelegateForm.HelperName"/> for why they must never be appended to a
        /// §12.4 name set.
        /// </summary>
        internal static IReadOnlyList<string> RequiredHelperNames(NetSurface surface) =>
            RequiredForms(surface).Select(f => f.HelperName).ToList();
    }
}
