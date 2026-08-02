using System.Collections.Generic;
using BasicLang.Compiler.AST;

namespace BasicLang.Net
{
    /// <summary>
    /// P2a-2 Task 2 — the analyzer→IRBuilder hand-off for resolved .NET members.
    ///
    /// <para><b>Why a side table and not a field on the AST node.</b> The analyzer and
    /// <c>IRBuilder</c> walk the SAME AST object graph separately (see
    /// <c>Compiler.CompileUnit</c>: <c>new IRBuilder(analyzer).Build(unit.AST, …)</c>), so a
    /// dictionary keyed on the expression node by REFERENCE identity carries the analyzer's
    /// resolution to the builder without adding .NET-specific state to every AST node. AST nodes
    /// have no value equality — reference identity is the correct and only key, and
    /// <see cref="ReferenceEqualityComparer"/> makes that explicit (and immune to anyone later
    /// giving a node type an <c>Equals</c> override).</para>
    ///
    /// <para><b>Recording is not reporting.</b> The analyzer records a descriptor whenever its
    /// warning-only probe resolves a member (<c>SemanticAnalyzer.ProbeNetMemberAccess</c>);
    /// severity and diagnostics are untouched. Overload-precise selection replaces the
    /// name-match recording in P2a-2 Task 4; a name-unique member is already exact.</para>
    ///
    /// <para><b>"Resolved" does NOT imply "annotated" — the probe suppresses recording in every
    /// one of these cases, and downstream consumers (the P2a-2 Task-3 surface collector first
    /// among them) must not assume a .NET-looking access reaches this table:</b></para>
    /// <list type="bullet">
    /// <item><description><b>Claimed names and claimed calls</b> (spec §6.5's predicate, rows
    /// (a)/(b)/(c)) — <c>Console.WriteLine</c>, <c>List(Of T)</c>, every natively-handled
    /// spelling returns before the probe resolves anything. This is what keeps every existing
    /// program's surface empty.</description></item>
    /// <item><description><b><c>NativeBclSurface</c>-owned members</b> — the P1 six's native
    /// member surface is consulted first; a member the native runtime demonstrably implements
    /// is never recorded.</description></item>
    /// <item><description><b><c>System.Object</c> members</b> (<c>x.ToString()</c>,
    /// <c>GetHashCode</c>, …) — deliberately absent from <c>NetTypeResolver.GetMembers</c>
    /// (§7.2 excludes them unless overridden), so the probe's early-out skips both the warning
    /// AND the recording. The D-P1 two-name allowlist (Task 4 Step 2a) lifts this for exactly
    /// <c>ToString</c>/<c>GetHashCode</c>.</description></item>
    /// <item><description><b>Unresolvable and zero-surface types</b> — a receiver type the
    /// resolver cannot resolve, or one whose member list comes back empty, records
    /// nothing.</description></item>
    /// <item><description><b>Compilations without a <c>NetResolverFactory</c></b> — every
    /// C#-backend build and the LSP today; the probe is inert without one.</description></item>
    /// </list>
    ///
    /// <para>Owned by one <c>SemanticAnalyzer</c>, i.e. per compilation unit — exactly the
    /// lifetime of the AST whose nodes key it, so entries can never dangle into another
    /// compilation. INTERNAL on purpose: carriage adds nothing to the compiler's public API.</para>
    /// </summary>
    internal sealed class NetAstAnnotations
    {
        private readonly Dictionary<ExpressionNode, NetMemberDescriptor> _resolvedMembers =
            new Dictionary<ExpressionNode, NetMemberDescriptor>(ReferenceEqualityComparer.Instance);

        /// <summary>Resolved member per AST expression node (reference-keyed). Read by IRBuilder.</summary>
        internal IReadOnlyDictionary<ExpressionNode, NetMemberDescriptor> ResolvedMembers => _resolvedMembers;

        /// <summary>
        /// Records (or overwrites — last resolution wins, harmless because the analyzer resolves
        /// a given node deterministically) the member a node resolved to. Null node or member is
        /// ignored rather than thrown: a lost annotation degrades to the inert default
        /// downstream — the IR node keeps a null <c>ResolvedNetTarget</c> and the call is simply
        /// not part of the .NET surface — which is strictly safer than failing the compilation.
        /// </summary>
        internal void RecordResolvedMember(ExpressionNode node, NetMemberDescriptor member)
        {
            if (node == null || member == null)
                return;

            _resolvedMembers[node] = member;
        }
    }
}
