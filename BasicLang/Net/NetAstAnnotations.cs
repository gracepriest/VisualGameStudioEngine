using System;
using System.Collections.Generic;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen.Net;   // NetWrapperOrigin — §11.3's provenance element

namespace BasicLang.Net
{
    /// <summary>
    /// One recorded resolution: the descriptor plus whether its signature identity is EXACT
    /// (see <see cref="NetAstAnnotations.RecordResolvedMember"/> — the Task-7a name-only gate).
    /// </summary>
    internal readonly struct NetMemberAnnotation
    {
        public NetMemberAnnotation(NetMemberDescriptor member, bool exact)
        {
            Member = member;
            Exact = exact;
        }

        public NetMemberDescriptor Member { get; }

        /// <summary>False = name-only record; the lowering must refuse it.</summary>
        public bool Exact { get; }
    }

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
        private readonly Dictionary<ExpressionNode, NetMemberAnnotation> _resolvedMembers =
            new Dictionary<ExpressionNode, NetMemberAnnotation>(ReferenceEqualityComparer.Instance);

        /// <summary>Resolved member per AST expression node (reference-keyed). Read by IRBuilder.</summary>
        internal IReadOnlyDictionary<ExpressionNode, NetMemberAnnotation> ResolvedMembers => _resolvedMembers;

        /// <summary>
        /// Records (or overwrites — last resolution wins, harmless because the analyzer resolves
        /// a given node deterministically) the member a node resolved to. Null node or member is
        /// ignored rather than thrown: a lost annotation degrades to the inert default
        /// downstream — the IR node keeps a null <c>ResolvedNetTarget</c> and the call is simply
        /// not part of the .NET surface — which is strictly safer than failing the compilation.
        ///
        /// <para><b><paramref name="exact"/> is the Task-7a name-only gate's source bit.</b>
        /// True only when the descriptor's SIGNATURE is known to be the one the call selects —
        /// the overload probe's winner, a probed constructor, or a member with no overload axis
        /// (property/field). A first-name-match record stays false, and the C++ lowering
        /// refuses to lower it: a name-matched descriptor could be the wrong overload, and
        /// silently calling it is a miscompile, never a fallback.</para>
        /// </summary>
        internal void RecordResolvedMember(ExpressionNode node, NetMemberDescriptor member, bool exact)
        {
            if (node == null || member == null)
                return;

            _resolvedMembers[node] = new NetMemberAnnotation(member, exact);
        }

        // ------------------------------------------------------------------
        // P2a-2 Task 7b — §11.3 tier-1 provenance. THIS table is the only thing in the compiler
        // that knows both the resolved .NET member AND the source position of the BasicLang
        // expression that reached it: the IR keeps the descriptor but drops the AST node, and the
        // generated shim ILC reports against has no BasicLang position at all.
        // ------------------------------------------------------------------

        /// <summary>
        /// (mangled wrapper name → BasicLang call site) for every EXACT annotation in this table,
        /// as spec §11.3's tier-1 input. <c>NetShimGenerator.Provenance</c> intersects the result
        /// with the exports actually emitted, so offering an entry for a member that never becomes
        /// an export is harmless here.
        ///
        /// <para><b>Two mangles per property/field, one node.</b> A member READ lowers to the
        /// getter-shaped slot and a member WRITE to
        /// <see cref="NetAccessorSynthesis.SetterFor"/>'s synthesized <c>set_X</c> — two different
        /// exports — but the analyzer records ONE annotation per AST node and cannot know which
        /// the builder will produce. Emitting both keys against the same location is correct for
        /// either lowering and costs one dictionary entry; emitting only the getter would leave
        /// every property-write wrapper unattributed at tier 1, which is exactly the shape §11.3's
        /// worked example is about.</para>
        ///
        /// <para><b>Non-exact annotations are skipped</b>, mirroring the surface collector's
        /// <c>IsCollectable</c>: a name-only record is refused by the C++ lowering, so it can never
        /// correspond to an emitted wrapper and an entry for it could only ever mis-attribute.</para>
        ///
        /// <para><b>Yielded in SOURCE ORDER, and that is a correctness requirement, not tidiness.</b>
        /// The backing dictionary is keyed by AST node REFERENCE, so its enumeration order follows
        /// address-derived hash codes and differs run to run. Combined with
        /// <see cref="NetProvenanceMap"/>'s last-write-wins rule, a member called from two sites in
        /// one file would get a different <c>.bas</c> line in each build — a BL6020 that moves on
        /// its own. Sorting by <c>(Line, Column)</c> makes the choice a STATED rule: <b>the LAST
        /// call site in source order is the one blamed.</b> Arbitrary between the two, but stable,
        /// and stable is the whole property that was missing. (Across units the later unit still
        /// wins, which is deterministic because unit order is.)</para>
        /// </summary>
        /// <param name="filePath">The compilation unit's path — what a BL6020 shows the user.</param>
        /// <param name="lineOffset">
        /// The <c>.mod</c>/<c>.cls</c> wrapper offset (<c>CompilationUnit.LineOffset</c>). AST
        /// nodes carry lines in the WRAPPED source; the analyzer's own errors are un-offset the
        /// same way in <c>BasicCompiler.CompileUnit</c>, and a BL6020 pointing one line past the
        /// end of a two-line <c>.cls</c> is worse than no line at all.
        /// </param>
        internal IEnumerable<KeyValuePair<string, NetWrapperOrigin>> CallSiteOrigins(
            string filePath, int lineOffset)
        {
            // Materialized and sorted rather than streamed straight off the dictionary — see the
            // remarks: reference-keyed enumeration order is an allocation accident, and downstream
            // is last-write-wins.
            var ordered = new List<KeyValuePair<ExpressionNode, NetMemberAnnotation>>(_resolvedMembers);
            ordered.Sort((a, b) =>
            {
                var byLine = a.Key.Line.CompareTo(b.Key.Line);
                return byLine != 0 ? byLine : a.Key.Column.CompareTo(b.Key.Column);
            });

            foreach (var entry in ordered)
            {
                if (!entry.Value.Exact) continue;
                var member = entry.Value.Member;

                var line = entry.Key.Line > 0 ? Math.Max(1, entry.Key.Line - lineOffset) : 0;
                var origin = NetWrapperOrigin.BasCallSite(
                    filePath ?? string.Empty, line, entry.Key.Column,
                    member.DeclaringTypeFullName + "." + member.Name);

                yield return new KeyValuePair<string, NetWrapperOrigin>(
                    NetNameMangler.Mangle(member), origin);

                // The write half. SetterFor refuses indexers (§8.5 is Task 9's), so the same
                // Parameters.Count == 0 clause IRBuilder uses before synthesizing guards it here.
                if ((member.Kind == NetMemberCategory.Property || member.Kind == NetMemberCategory.Field)
                    && member.Parameters.Count == 0)
                {
                    yield return new KeyValuePair<string, NetWrapperOrigin>(
                        NetNameMangler.Mangle(NetAccessorSynthesis.SetterFor(member)), origin);
                }
            }
        }

        // ------------------------------------------------------------------
        // P2a-2 Task 4 — resolved catch-clause exception types (spec §11.1's ladder-trigger
        // completion). A `Catch e As FileNotFoundException` clause whose type name is OUTSIDE
        // CppExceptionTypes' 12-name set but RESOLVES as a .NET exception type records the
        // resolver-supplied fully-qualified name here; IRBuilder stamps it onto the produced
        // IRCatchClause so the C++ ladder can emit an arm for it — without this the clause
        // silently binds to a later `Exception` clause. Keyed by CatchClauseNode reference
        // identity for the same reason the member table is.
        // ------------------------------------------------------------------

        private readonly Dictionary<CatchClauseNode, string> _resolvedExceptionTypes =
            new Dictionary<CatchClauseNode, string>(ReferenceEqualityComparer.Instance);

        /// <summary>Resolved .NET exception full name per catch clause. Read by IRBuilder.</summary>
        internal IReadOnlyDictionary<CatchClauseNode, string> ResolvedExceptionTypes =>
            _resolvedExceptionTypes;

        /// <summary>
        /// Records the fully-qualified .NET name a catch clause's exception type resolved to.
        /// Null-tolerant for the same lost-annotation-degrades-safely reason as
        /// <see cref="RecordResolvedMember"/>.
        /// </summary>
        internal void RecordResolvedExceptionType(CatchClauseNode node, string fullName)
        {
            if (node == null || string.IsNullOrEmpty(fullName))
                return;

            _resolvedExceptionTypes[node] = fullName;
        }
    }
}
