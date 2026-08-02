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
    /// severity and diagnostics are untouched. Claimed names (spec §6.5's predicate) never reach
    /// the probe, so they never appear here — which is what keeps every existing program's
    /// surface empty. Overload-precise selection replaces the name-match recording in P2a-2
    /// Task 4; a name-unique member is already exact.</para>
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
        /// ignored rather than thrown: the probe runs on the analysis hot path where a defensive
        /// no-op is worth more than an exception.
        /// </summary>
        internal void RecordResolvedMember(ExpressionNode node, NetMemberDescriptor member)
        {
            if (node == null || member == null)
                return;

            _resolvedMembers[node] = member;
        }
    }
}
