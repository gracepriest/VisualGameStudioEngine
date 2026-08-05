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
        /// Every managed helper name <see cref="RequiredForms"/> implies, in the same order.
        /// ⛔ These are shim-internal method names, NOT exports — see
        /// <see cref="NetDelegateForm.HelperName"/> for why they must never be appended to a
        /// §12.4 name set.
        /// </summary>
        internal static IReadOnlyList<string> RequiredHelperNames(NetSurface surface) =>
            RequiredForms(surface).Select(f => f.HelperName).ToList();
    }
}
