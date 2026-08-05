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
    internal static class NetDelegateDispatch
    {
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
        /// <para>Every slot is <c>BLNET_SLOT_VALUE</c> with size 0 because v1 admits blittable
        /// scalars only — the gate lives in <c>NetShimGenerator</c>'s dispatcher emission, which
        /// throws at generation time for anything else, so a non-scalar delegate never reaches
        /// here. <c>size</c> is documented as meaningful only for STRUCT and OUT.</para>
        /// </summary>
        internal static string CppSlotDescriptors(string invokeSignature)
        {
            var count = SlotCount(invokeSignature);
            if (count == 0) return null;

            return "{ " + string.Join(", ", Enumerable.Repeat("{BLNET_SLOT_VALUE, 0}", count)) + " }";
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
