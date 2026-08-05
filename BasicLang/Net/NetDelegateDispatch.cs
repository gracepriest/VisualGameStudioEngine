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
    /// <param name="ExportName">
    /// The dispatcher's export name — simultaneously the <c>BlnetProxyTable</c> slot, the shim's
    /// <c>EntryPoint</c> string, and part of §10.2's shim cache key.
    /// </param>
    internal sealed record NetDelegateForm(
        string DelegateFullName,
        string InvokeSignature,
        string ExportName);

    /// <summary>
    /// The single shared derivation of §8.4's delegate dispatchers — P2a-2 Task 11 Step 3,
    /// deliberately shaped after <see cref="NetArrayCopy"/>'s <c>RequiredForms</c>.
    ///
    /// <para><b>Why one function and not two.</b> §12.4 requires the proxy table's slots to EQUAL
    /// the shim's surface-derived exports, and it is enforced by a real set comparison
    /// (<c>NetShimGeneratorTests.ProxyTableSlotsMatchTheSurfaceDerivedExports</c>).
    /// <see cref="NetShimGenerator"/>'s own header closes with "Task 11 should read this before
    /// deciding how to carry the delegate dispatcher: '§12.4-exempt' is not an available answer."
    /// So both emitters call <see cref="RequiredExportNames"/> and the invariant holds by
    /// construction — a consequence of calling one function twice rather than a property anyone
    /// has to remember to re-check.</para>
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
        /// Every export/slot name <see cref="RequiredForms"/> implies, in the same order — the
        /// list both emitters append to their §12.4 name sets.
        /// </summary>
        internal static IReadOnlyList<string> RequiredExportNames(NetSurface surface) =>
            RequiredForms(surface).Select(f => f.ExportName).ToList();
    }
}
