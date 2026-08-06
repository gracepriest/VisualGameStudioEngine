using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BasicLang.Net
{
    /// <summary>
    /// Spec §6.1's THIRD question — "given this call site with these argument types, which overload
    /// wins?" — split out of <c>NetTypeResolver.cs</c> so that file stays about the other two (does
    /// this type exist, what members does it have).
    ///
    /// <para><b>This half reaches REAL C# overload resolution</b>, because Roslyn's own resolver
    /// (<c>OverloadResolution</c>, <c>MemberResolutionResult</c>) is internal to
    /// <c>Microsoft.CodeAnalysis.CSharp</c> and no public API resolves an overload from a bag of
    /// type symbols. What IS public is the binder, via <see cref="SemanticModel"/> -- so a C#
    /// fragment that makes the call is synthesized, added to the resolver's compilation, and the
    /// semantic model is asked which symbol the call bound to. Implicit conversions, C# §12.6.4
    /// betterness, <c>params</c>, optional parameters and generic inference are therefore not
    /// implemented here at all. See <see cref="NetTypeResolver.ResolveOverload"/> for the full
    /// contract, what it does not cover, and why each gate is load-bearing.</para>
    ///
    /// <para><b>Two inbound dependencies only</b>, both in the other partial:
    /// <see cref="NetTypeResolver.Lookup"/> for the receiver symbol and
    /// <see cref="NetTypeResolver.CandidateMembers"/> for the candidate set. The second is the seam
    /// that keeps spec §7.2's walk, exclusion rules and duplicate collapse in exactly ONE place —
    /// re-walking derived-to-base here would re-meet the duplicate-override problem and report a
    /// spurious ambiguity on an ordinary <c>fs.Read(buf, 0, n)</c>.</para>
    /// </summary>
    internal sealed partial class NetTypeResolver
    {
        // ------------------------------------------------------------------
        // Overload resolution (spec §6.1's third question)
        // ------------------------------------------------------------------

        /// <summary>
        /// Identifiers inside the synthesized probe. Deliberately un-Basic-like and prefixed, so
        /// that no type, namespace or member in any referenced assembly can shadow them.
        /// </summary>
        private const string ProbeClassName = "__BlnetOverloadProbe";
        private const string ProbeMethodName = "__BlnetProbe";
        private const string ProbeReceiverName = "__blnetReceiver";
        private const string ProbeArgumentPrefix = "__blnetArgument";

        /// <summary>"The call is ambiguous between the following methods or properties" — the ONLY
        /// signal that separates <see cref="NetOverloadOutcome.Ambiguous"/> from
        /// <see cref="NetOverloadOutcome.NoMatch"/>. Both arrive as
        /// <c>CandidateReason.OverloadResolutionFailure</c> with a populated candidate list, so the
        /// candidate list cannot tell them apart.</summary>
        private const string AmbiguousCallDiagnosticId = "CS0121";

        /// <summary>"Member cannot be accessed with an instance reference; qualify it with a type
        /// name instead" — C# forbids what VB permits, so this is the retry signal, not an
        /// answer.</summary>
        private const string StaticThroughInstanceDiagnosticId = "CS0176";

        /// <summary>
        /// Cap on how many by-ref keyword shapes one call site is probed with (see
        /// <see cref="RefKindKeywordMasks"/>). Each shape costs one bind, ~2 ms. Real overload sets
        /// need one or two; the cap only bounds a pathological type with many distinct
        /// <c>ref</c>/<c>out</c> layouts under one name.
        /// </summary>
        private const int MaxRefKindMasks = 4;

        /// <summary>
        /// The reserved argument spelling for the <c>Nothing</c>/null literal — §6.5:
        /// "<c>Nothing</c> participates as a null literal" (the P2a-2 Task-7a probe extension;
        /// Task 4 had recorded the gap as a pin). Passed VERBATIM as the C# <c>null</c> literal
        /// at the synthesized call site, so Roslyn applies the REAL null-literal conversion and
        /// betterness rules — never declared as a probe parameter (null has no type to declare)
        /// and never combined with a by-ref keyword (C# cannot pass null by ref, so
        /// <see cref="RefKindKeywordMasks"/> forces the by-value shape at null positions).
        /// <c>"null"</c> is a C# keyword: no well-formed type spelling can collide with it, and
        /// <see cref="IsOneWellFormedTypeName"/> would reject it — which is why the validation
        /// loop admits it explicitly for ARGUMENTS and never for type arguments.
        /// </summary>
        public const string NullArgumentSpelling = "null";

        private static bool IsNullSpelling(string spelling) =>
            string.Equals(spelling, NullArgumentSpelling, StringComparison.Ordinal);

        private const string LambdaSpellingPrefix = "lambda(";

        /// <summary>
        /// The reserved argument spelling for a BasicLang LAMBDA of <paramref name="arity"/>
        /// parameters — §8.4, decision D-P11.
        ///
        /// <para><b>Why a lambda has no type spelling.</b> A BasicLang lambda types structurally
        /// as <c>Func</c>/<c>Action</c>, while real .NET delegate parameters are NAMED types
        /// (<c>MatchEvaluator</c>, <c>Comparison&lt;T&gt;</c>, <c>ThreadStart</c>). Matching those
        /// nominally admits none of the APIs anyone actually calls. So a lambda is TARGET-TYPED
        /// against each candidate, exactly as C# does it — and because this probe resolves
        /// overloads by synthesizing source and letting the real C# compiler answer, target-typing
        /// costs nothing but emitting a lambda expression instead of a declared local.</para>
        ///
        /// <para>Reserved like <see cref="NullArgumentSpelling"/> and screened out of the
        /// well-formed-type-name validation for the same reason: it is an ARGUMENT spelling, not a
        /// type name. <c>lambda(</c> cannot collide with a real spelling — no well-formed C# type
        /// name contains a parenthesis.</para>
        /// </summary>
        public static string LambdaArgumentSpelling(int arity)
        {
            if (arity < 0)
                throw new ArgumentOutOfRangeException(nameof(arity));

            return LambdaSpellingPrefix
                 + arity.ToString(System.Globalization.CultureInfo.InvariantCulture)
                 + ")";
        }

        private static bool TryLambdaArity(string spelling, out int arity)
        {
            arity = 0;
            if (spelling == null
                || !spelling.StartsWith(LambdaSpellingPrefix, StringComparison.Ordinal)
                || !spelling.EndsWith(")", StringComparison.Ordinal))
                return false;

            var digits = spelling.Substring(
                LambdaSpellingPrefix.Length,
                spelling.Length - LambdaSpellingPrefix.Length - 1);

            return int.TryParse(
                digits,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out arity);
        }

        private static bool IsLambdaSpelling(string spelling) => TryLambdaArity(spelling, out _);

        /// <summary>
        /// The lambda expression synthesized for a §8.4 lambda argument.
        ///
        /// <para>The body is <c>throw null!</c> and not <c>default</c> deliberately: a throw
        /// expression converts to ANY delegate return type <b>including void</b>, so one spelling
        /// serves <c>Action</c>-shaped and <c>Func</c>-shaped targets alike. A <c>default</c> body
        /// would silently fail to convert to every void-returning delegate — i.e. it would answer
        /// "no such overload" for <c>ThreadStart</c> and friends.</para>
        ///
        /// <para>Parameters are implicitly typed, which is what forces C# to target-type them from
        /// the candidate's delegate signature. Names are slot- AND position-qualified so two
        /// lambda arguments in one call cannot collide.</para>
        /// </summary>
        private static string LambdaExpression(string spelling, int slot)
        {
            TryLambdaArity(spelling, out var arity);

            var names = Enumerable.Range(0, arity)
                .Select(p => ProbeArgumentPrefix + slot + "_p" + p.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

            return "(" + string.Join(", ", names) + ") => throw null!";
        }

        /// <summary>
        /// Overload results, keyed on the whole request. Separate from <see cref="_cache"/> because
        /// the key is a request, not a type name. Concurrent for the reason <see cref="_cache"/>
        /// documents, and BOUNDED by <see cref="MaxOverloadCacheEntries"/> — see there for why this
        /// one needed a cap more urgently than <see cref="_cache"/> did.
        /// </summary>
        private readonly ConcurrentDictionary<string, NetOverloadResult> _overloadCache =
            new ConcurrentDictionary<string, NetOverloadResult>(StringComparer.Ordinal);

        /// <summary>
        /// Cap on <see cref="_overloadCache"/>. Measured during Task 5, this cache grows worse than
        /// <see cref="_cache"/> in three independent ways, which is why it is the one that had to be
        /// bounded:
        /// <list type="bullet">
        /// <item><description>The key is a whole REQUEST — call form, type, member, type arguments
        /// and every argument spelling — so entry count is COMBINATORIAL in half-typed identifiers
        /// rather than linear in names. On the IntelliSense path, typing <c>Regex.IsMatch(</c> one
        /// character at a time yields a distinct entry per keystroke.</description></item>
        /// <item><description>Entries are created BEFORE the spellings are validated (deliberately —
        /// see <see cref="ResolveOverload"/>'s key comment), so every malformed spelling buys a
        /// permanent <see cref="NetOverloadOutcome.NoMatch"/> entry. Rejecting a spelling costs a
        /// cache slot.</description></item>
        /// <item><description>Entries carry a <see cref="NetMemberDescriptor"/> with its own
        /// <see cref="NetMemberDescriptor.Parameters"/> list, so they are materially larger than a
        /// lookup entry's (outcome + a symbol reference Roslyn already holds).</description></item>
        /// </list>
        ///
        /// <para><b>Bounded rather than key-reordered.</b> The obvious alternative — validate the
        /// spellings first so invalid input never keys an entry — was rejected: it moves the
        /// invalid-input path from cached to recomputed at ~2 ms per probe, and invalid input is
        /// exactly what a half-typed call site produces on every keystroke. Capping keeps the
        /// pathological population out of memory without making it slow.</para>
        ///
        /// <para><b>Cleared wholesale, not evicted LRU.</b> A
        /// <see cref="ConcurrentDictionary{TKey, TValue}"/> carries no recency order to evict by, and
        /// adding that bookkeeping puts a lock on the path this cache exists to keep fast. The
        /// keystroke-driven entries that fill it are precisely the ones never asked for again, so a
        /// clear discards mostly-dead weight; a genuine call site costs one ~2 ms re-probe.</para>
        /// </summary>
        internal const int MaxOverloadCacheEntries = 4096;

        /// <summary>Live entry count, for the bound's test. Not part of the facade.</summary>
        internal int OverloadCacheCount => _overloadCache.Count;

        private static readonly NetOverloadResult NoOverloadMatch =
            new NetOverloadResult(NetOverloadOutcome.NoMatch, null);

        private static readonly NetOverloadResult AmbiguousOverload =
            new NetOverloadResult(NetOverloadOutcome.Ambiguous, null);

        private static readonly NetOverloadResult TypeUnavailableForOverload =
            new NetOverloadResult(NetOverloadOutcome.TypeUnavailable, null);

        /// <summary>
        /// Which member of <paramref name="typeFullName"/> a call with these argument types selects
        /// — spec §6.1's third question, and the one §6.1 says "cannot be approximated".
        ///
        /// <para><b>This is REAL C# overload resolution, reached the only way it can be.</b> Roslyn
        /// exposes no public API that resolves an overload from a bag of type symbols —
        /// <c>OverloadResolution</c> and its <c>MemberResolutionResult</c> are internal to
        /// <c>Microsoft.CodeAnalysis.CSharp</c>. What IS public is the binder, via
        /// <see cref="SemanticModel"/>. So this method SYNTHESIZES a C# fragment that makes the
        /// call — a static class with one method whose parameters have exactly the argument types,
        /// whose body performs the invocation — adds it to the resolver's compilation, and asks the
        /// semantic model which symbol the invocation bound to. Implicit conversions, C# §12.6.4
        /// betterness, <c>params</c> expansion, optional parameters, generic method type inference
        /// and the inherited-member lookup are therefore not implemented here at all; they are
        /// performed by the same code that would compile the equivalent C#.</para>
        ///
        /// <para><b>What that buys, concretely.</b> <c>Console.WriteLine(s)</c> with a
        /// <c>Short</c> resolves to <c>WriteLine(Int32)</c>, and <c>Math.Abs(b)</c> with a
        /// <c>Byte</c> resolves to <c>Abs(Int16)</c> rather than <c>Abs(Int32)</c>. Both are
        /// ordinary BasicLang; an exact-match matcher answers "no such overload" for the first and
        /// silently picks the wrong member for the second. The first would be a BL6017 warning on a
        /// program that compiles clean on the C# backend — which spec §6.3's "valid programs behave
        /// identically on both backends" forbids — and the second is worse than a warning, because
        /// Task 12 would emit an export for a method the call does not reach.</para>
        ///
        /// <para><b>What it does NOT cover, stated plainly.</b> Extension methods: the probe
        /// declares no <c>using</c>, so nothing in <c>System.Linq</c> is in scope and an extension
        /// method is never selected. That is deliberate — an extension method is not a member of
        /// the type (§7.2), so there is nothing for a shim export to bind to. A member whose return
        /// type is a POINTER cannot be bound in the resolver's compilation (which does not allow
        /// unsafe code) and reports <see cref="NetOverloadOutcome.NoMatch"/>; pointers are outside
        /// §8.3's marshaling table anyway. Explicit method type arguments have no input here —
        /// inference from the arguments is the only path, so <c>Array.Empty</c>, whose <c>T</c> is
        /// not inferable, is <see cref="NetOverloadOutcome.NoMatch"/>.</para>
        ///
        /// <para><b>A KNOWN DISAGREEMENT WITH <see cref="GetMembers"/>, recorded rather than
        /// hidden.</b> An <c>[Obsolete(error: true)]</c> member is compiled as an ERROR (CS0619), so
        /// the <c>errors.Count == 0</c> gate reports it <see cref="NetOverloadOutcome.NoMatch"/> —
        /// while §7.2 includes <c>[Obsolete]</c> members in the surface and
        /// <see cref="GetMembers"/> duly lists it. The two therefore describe the same member
        /// differently. The direction is safe: the C# backend rejects those calls too (SYSLIB0011 is
        /// an error on .NET 8), so nothing that compiles today becomes unresolvable. But Task 12
        /// must not assume a member in the surface is necessarily resolvable at a call site.</para>
        ///
        /// <para><b>Spellings, and the two grammars.</b> <paramref name="typeFullName"/> is the same
        /// spelling <see cref="ResolveType"/> and <see cref="GetMembers"/> take — a metadata name,
        /// generic arity required (<c>Queue`1</c>). <paramref name="argumentTypeFullNames"/> and
        /// <paramref name="typeArgumentFullNames"/> are instead C# type syntax, fully qualified:
        /// <c>System.Int32</c>, <c>System.String[]</c>,
        /// <c>System.Collections.Generic.List&lt;System.Int32&gt;</c>,
        /// <c>System.Environment.SpecialFolder</c>. That is not an arbitrary second dialect — it is
        /// exactly what <see cref="TypeName"/> emits, so
        /// <see cref="NetMemberDescriptor.ParameterTypeFullNames"/> feeds straight back in here
        /// (pinned by
        /// <c>ResolveOverload_AcceptsTheArgumentSpellingsTheResolverItselfProduces</c>).</para>
        ///
        /// <para><b>Every spelling is validated before it can reach the synthesized source.</b>
        /// These strings originate in BasicLang source, and this method puts them into C# that is
        /// then compiled. An argument spelling must parse as exactly one complete type name and a
        /// member name must be a C# identifier, or the answer is
        /// <see cref="NetOverloadOutcome.NoMatch"/> — so <c>"Int32) ; static void Boom("</c> cannot
        /// become a second method in the probe.</para>
        ///
        /// <para><b>Type arguments are REQUIRED for a generic type, never guessed</b>, for the same
        /// reason <see cref="ResolveType"/> requires arity: <c>Queue`1.Enqueue(T)</c> can only be
        /// matched against an <c>Integer</c> argument once the receiver is CONSTRUCTED, and
        /// substituting a guess would fabricate a binding. A generic type queried without them is
        /// <see cref="NetOverloadOutcome.NoMatch"/>. Consequence worth stating: a type parameter's
        /// bare <c>T</c> spelling in <see cref="NetMemberDescriptor"/> is never an INPUT to matching
        /// — it exists for §7.3's mangler and for diagnostics — so the "is <c>T</c> a type parameter
        /// or a global type named T" ambiguity cannot affect an answer here.</para>
        ///
        /// <para><b>BasicLang has no call-site by-ref keyword; C# requires one.</b>
        /// <c>Integer.TryParse(s, n)</c> is how the language spells a call to
        /// <c>TryParse(String, out Int32)</c>, but the same C# is CS1620. The probe therefore
        /// supplies the keywords the CANDIDATES declare, read off
        /// <see cref="NetParameterDescriptor.RefKind"/> — see
        /// <see cref="RefKindKeywordMasks"/>. The all-by-value shape is always tried FIRST, so a
        /// by-value overload wins over a by-ref one, matching BasicLang's <c>ByVal</c>
        /// default.</para>
        ///
        /// <para><b><see cref="NetCallForm.Instance"/> tolerates a Shared member.</b> VB permits
        /// <c>obj.SharedMethod()</c>; C# answers CS0176. On that diagnostic the probe is retried in
        /// the static form, because otherwise <c>r.IsMatch(input, pattern)</c> on a <c>Regex</c>
        /// variable would draw a BL6017 on a valid program. The reverse leniency does NOT exist —
        /// naming an instance member through its type is an error in VB too, so
        /// <see cref="NetCallForm.Static"/> never falls back.</para>
        ///
        /// <para><b>The winner must be a member of §7.2's surface.</b> The candidate set comes from
        /// <see cref="CandidateMembers"/> — the same walk, exclusions and duplicate collapse
        /// <see cref="GetMembers"/> uses — and the bound symbol is mapped back through it by
        /// <c>OriginalDefinition</c>. Two consequences, both deliberate. An override and the member
        /// it overrides are ONE candidate, so <c>fs.Read(buf, 0, n)</c> resolves instead of
        /// reporting a spurious ambiguity. And a member C# binds happily but §7.2 excludes —
        /// <c>System.Object.Equals(Object)</c> on a type that does not override it — is
        /// <see cref="NetOverloadOutcome.NoMatch"/>, because there is no member for a shim export to
        /// bind to; that is the one shape where a valid program draws a BL6017, and it is recorded
        /// rather than papered over.</para>
        ///
        /// <para><b>Not the type-existence question.</b> A <paramref name="typeFullName"/> that is
        /// unknown, ambiguous, or resolved but not effectively public answers
        /// <see cref="NetOverloadOutcome.TypeUnavailable"/> — never
        /// <see cref="NetOverloadOutcome.NoMatch"/>. Those are BL6016 and BL6023, and §6.5 says
        /// outright that "BL6018 covers ambiguous <i>overloads</i> only", so a member-level answer
        /// would misreport all three. Ask <see cref="ResolveTypeDetailed"/> for which one it
        /// was.</para>
        ///
        /// <para><b>There is no spelling for "I do not know this argument's type."</b> A null, empty
        /// or unresolvable argument spelling answers <see cref="NetOverloadOutcome.NoMatch"/>. A
        /// wildcard was considered and rejected: it can only widen the candidate set, so it either
        /// fabricates a binding — the exact defect this class exists to remove — or turns a valid
        /// call into a spurious BL6018. A caller that cannot type every argument must not ask.
        /// The one non-type spelling is <see cref="NullArgumentSpelling"/> — the KNOWN null
        /// literal (§6.5), which is not a guess: Roslyn applies the real null-conversion and
        /// betterness rules, and an over-wide candidate set surfaces honestly as CS0121 →
        /// <see cref="NetOverloadOutcome.Ambiguous"/>.</para>
        ///
        /// <para><b>Cost: ~2 ms per distinct call site, then cached.</b> Each probe parses a tiny
        /// tree, derives a compilation from this one (cheap — the reference manager and every
        /// metadata symbol are shared, which is also why <c>OriginalDefinition</c> compares equal
        /// across the two) and binds one statement. Measured at 1.9 ms over a 169-assembly
        /// framework closure. Repeat call sites are free; a by-ref overload set costs one extra
        /// probe.</para>
        ///
        /// <para><see cref="Diagnostics"/> is NOT touched. The synthesized fragment's compiler
        /// errors are the answer to the question, not a finding about the user's program — CS1503
        /// must never reach a BasicLang build log.</para>
        /// </summary>
        /// <param name="memberName">
        /// The member's metadata name. Not read when <paramref name="form"/> is
        /// <see cref="NetCallForm.Constructor"/>; pass <c>".ctor"</c> for symmetry with
        /// <see cref="NetMemberDescriptor.Name"/>.
        /// </param>
        public NetOverloadResult ResolveOverload(
            string typeFullName,
            NetCallForm form,
            string memberName,
            IReadOnlyList<string> argumentTypeFullNames,
            IReadOnlyList<string> typeArgumentFullNames = null)
        {
            var arguments = argumentTypeFullNames ?? Array.Empty<string>();
            var typeArguments = typeArgumentFullNames ?? Array.Empty<string>();

            // U+0001 separates the fields of the request, U+0002 precedes each list element, and
            // U+0003 stands in for a null spelling so a null cannot key the same entry as an empty
            // string. None of the three can occur in a type or member name, which is what keeps
            // two different requests from sharing one entry.
            //
            // EACH LIST ELEMENT IS PREFIXED, NOT JOINED BETWEEN. string.Join returns "" for BOTH
            // an empty list and a single empty element, so a joined key cannot tell
            // ResolveOverload(t, f, m, []) apart from ResolveOverload(t, f, m, [""]) -- and those
            // have DIFFERENT correct answers, because "" is not a well-formed type name and the
            // second must therefore be NoMatch. The key is computed BEFORE the spellings are
            // validated, so a collision there would return a cached zero-argument Resolved answer
            // for a call whose argument the caller could not type -- fabricating exactly the
            // binding this class exists to remove. Prefixing keys [] as "" and [""] as "\u0002".
            var key = string.Join("\u0001",
                new[]
                {
                    form.ToString(),
                    typeFullName ?? "\u0003",
                    memberName ?? "\u0003",
                    string.Concat(typeArguments.Select(a => "\u0002" + (a ?? "\u0003"))),
                    string.Concat(arguments.Select(a => "\u0002" + (a ?? "\u0003"))),
                });

            if (_overloadCache.TryGetValue(key, out var cached))
                return cached;

            var result = ResolveOverloadUncached(typeFullName, form, memberName, arguments, typeArguments);

            // Bound BEFORE inserting, so the cap is a real ceiling rather than a ceiling plus one
            // per racing writer. See MaxOverloadCacheEntries for why a wholesale clear beats an LRU
            // eviction here. Concurrent writers may both observe the cap and both clear; that is
            // harmless — the cache holds no state that has to survive, only work already done.
            if (_overloadCache.Count >= MaxOverloadCacheEntries)
                _overloadCache.Clear();

            _overloadCache[key] = result;
            return result;
        }

        private NetOverloadResult ResolveOverloadUncached(
            string typeFullName,
            NetCallForm form,
            string memberName,
            IReadOnlyList<string> arguments,
            IReadOnlyList<string> typeArguments)
        {
            // A type-level failure is NOT an overload answer. Lookup returns null for both NotFound
            // (BL6016) and Ambiguous (BL6023); reporting NoMatch would make Task 8 say "no matching
            // overload" for a type that was never found. Ask ResolveTypeDetailed for which it was.
            var type = Lookup(typeFullName).Symbol;
            if (type == null)
                return TypeUnavailableForOverload;

            // Lookup does not filter accessibility -- an `internal` type in a referenced assembly
            // resolves here. Probing it would draw CS0122 and read as "no such overload"; the truth
            // is that the type cannot be named at all, so a shim referencing it fails in csc. That
            // is a type-level answer (BL6016), which is why IsEffectivelyPublic is consulted before
            // any candidate is considered rather than only when a descriptor is built.
            if (!IsEffectivelyPublic(type))
                return TypeUnavailableForOverload;

            var isConstructor = form == NetCallForm.Constructor;
            if (!isConstructor && !SyntaxFacts.IsValidIdentifier(memberName))
                return NoOverloadMatch;

            foreach (var spelling in arguments)
            {
                // The null literal is a legal ARGUMENT spelling (§6.5), not a type name.
                if (IsNullSpelling(spelling))
                    continue;
                // So is a lambda (§8.4 / D-P11): it has no independent .NET type at all, and is
                // target-typed against each candidate by the synthesized source below.
                if (IsLambdaSpelling(spelling))
                    continue;
                if (!IsOneWellFormedTypeName(spelling))
                    return NoOverloadMatch;
            }
            foreach (var spelling in typeArguments)
            {
                // A type ARGUMENT can never be null — reject the sentinel here like any
                // other malformed spelling.
                if (!IsOneWellFormedTypeName(spelling))
                    return NoOverloadMatch;
            }

            var receiver = ReceiverSyntax(type, typeArguments);
            if (receiver == null)
                return NoOverloadMatch;   // a generic type without its type arguments

            var wanted = isConstructor ? NetMemberCategory.Constructor : NetMemberCategory.Method;
            var candidates = CandidateMembers(typeFullName)
                .Where(m => m.Descriptor.Kind == wanted
                            && (isConstructor
                                || string.Equals(m.Descriptor.Name, memberName, StringComparison.Ordinal)))
                .ToList();

            // No candidate in §7.2's surface can win, so there is nothing to probe for. This also
            // short-circuits every member the surface excludes on purpose — System.Object's own
            // members, accessors, operators, the synthesized value-type constructor.
            if (candidates.Count == 0)
                return NoOverloadMatch;

            // A static class cannot be spelled as a parameter type in C# (CS0721), so there is no
            // instance form to probe for Console/Math/Path.
            if (form == NetCallForm.Instance && type.IsStatic)
                form = NetCallForm.Static;

            var masks = RefKindKeywordMasks(candidates, arguments);
            var probe = Probe(receiver, memberName, arguments, masks, form);

            // VB permits a Shared member through an instance receiver; C# does not.
            if (probe.Winner == null && form == NetCallForm.Instance && probe.SawStaticThroughInstance)
                probe = Probe(receiver, memberName, arguments, masks, NetCallForm.Static);

            if (probe.Winner == null)
                return probe.SawAmbiguity ? AmbiguousOverload : NoOverloadMatch;

            // OriginalDefinition, not the symbol itself: a constructed generic's member
            // (Queue<Int32>.Enqueue(Int32), Task.FromResult<Int32>(Int32)) is a different symbol
            // from the definition CandidateMembers walked. Comparing the constructed form makes
            // every generic call NoMatch.
            var definition = probe.Winner.OriginalDefinition;
            foreach (var candidate in candidates)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate.Symbol, definition))
                    return new NetOverloadResult(NetOverloadOutcome.Resolved, candidate.Descriptor);
            }

            return NoOverloadMatch;
        }

        private readonly struct ProbeOutcome
        {
            public ProbeOutcome(IMethodSymbol winner, bool sawAmbiguity, bool sawStaticThroughInstance)
            {
                Winner = winner;
                SawAmbiguity = sawAmbiguity;
                SawStaticThroughInstance = sawStaticThroughInstance;
            }

            public IMethodSymbol Winner { get; }

            public bool SawAmbiguity { get; }

            public bool SawStaticThroughInstance { get; }
        }

        /// <summary>
        /// Probes each by-ref keyword shape in order and returns the first winner, accumulating the
        /// retry signals from the shapes that lost so a caller can tell ambiguity from absence.
        /// </summary>
        private ProbeOutcome Probe(
            string receiver,
            string memberName,
            IReadOnlyList<string> arguments,
            IReadOnlyList<string[]> masks,
            NetCallForm form)
        {
            var sawAmbiguity = false;
            var sawStaticThroughInstance = false;

            foreach (var mask in masks)
            {
                var attempt = ProbeOnce(receiver, memberName, arguments, mask, form);
                if (attempt.Winner != null)
                    return attempt;
                sawAmbiguity |= attempt.SawAmbiguity;
                sawStaticThroughInstance |= attempt.SawStaticThroughInstance;
            }

            return new ProbeOutcome(null, sawAmbiguity, sawStaticThroughInstance);
        }

        /// <summary>
        /// One bind of one synthesized call. The probe is a static class so nothing can construct
        /// it, and its single method's PARAMETERS carry the argument types — a parameter is always
        /// "definitely assigned", which is what lets an <c>out</c>/<c>ref</c> keyword be applied to
        /// it without the CS0165 a local would draw.
        /// </summary>
        private ProbeOutcome ProbeOnce(
            string receiver,
            string memberName,
            IReadOnlyList<string> arguments,
            string[] mask,
            NetCallForm form)
        {
            var declared = new List<string>();
            if (form == NetCallForm.Instance)
                declared.Add(receiver + " " + ProbeReceiverName);
            for (var i = 0; i < arguments.Count; i++)
            {
                // A null argument is passed as the literal, not through a declared
                // parameter — null has no type to declare. Indices stay stable: the
                // parameter for slot i keeps the i-suffixed name whether or not earlier
                // slots were null.
                // A lambda is likewise not declared — it has no type to declare, and declaring one
                // would defeat the target-typing that makes it bind (§8.4 / D-P11).
                if (!IsNullSpelling(arguments[i]) && !IsLambdaSpelling(arguments[i]))
                    declared.Add(arguments[i] + " " + ProbeArgumentPrefix + i);
            }

            var passed = string.Join(", ",
                Enumerable.Range(0, arguments.Count).Select(i =>
                    IsNullSpelling(arguments[i])
                        ? NullArgumentSpelling
                        : IsLambdaSpelling(arguments[i])
                            ? LambdaExpression(arguments[i], i)
                            : mask[i] + ProbeArgumentPrefix + i));

            // A discard rather than `object x = new ...`: boxing a `ref struct` would be CS0029 and
            // would read as "no such constructor" instead of the §8.3 marshaling answer it is.
            var call = form == NetCallForm.Constructor
                ? "_ = new " + receiver + "(" + passed + ");"
                : (form == NetCallForm.Instance ? ProbeReceiverName : receiver)
                  + "." + memberName + "(" + passed + ");";

            var source = "static class " + ProbeClassName
                       + " { static void " + ProbeMethodName + "(" + string.Join(", ", declared) + ")"
                       + " { " + call + " } }";

            var tree = CSharpSyntaxTree.ParseText(source);
            var model = _compilation.AddSyntaxTrees(tree).GetSemanticModel(tree);
            var callSite = tree.GetRoot().DescendantNodes().FirstOrDefault(
                n => n is InvocationExpressionSyntax || n is ObjectCreationExpressionSyntax);
            if (callSite == null)
                return default;   // unreachable for a validated request; never throw on this path

            // The fragment's compiler errors ARE the answer to the question, so they are read here
            // and never surfaced: CS1503 must not reach a BasicLang build log.
            //
            // Requiring the fragment to be error-FREE is load-bearing, not tidiness. Roslyn binds a
            // symbol for calls that are not actually callable -- `new` on an ABSTRACT type binds the
            // constructor and reports CS0144 -- so accepting a non-null Symbol on its own would
            // report Resolved for a member no shim could ever invoke.
            var errors = model.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Id)
                .ToList();

            if (errors.Count == 0 && model.GetSymbolInfo(callSite).Symbol is IMethodSymbol winner)
                return new ProbeOutcome(winner, false, false);

            // CS0121 is the ONLY signal that separates ambiguity from absence. The candidate list
            // cannot do it: measured, `new Regex(1, 2, 3)` -- an unambiguous no-match -- comes back
            // as CandidateReason.OverloadResolutionFailure with THREE CandidateSymbols, exactly like
            // a genuine tie. Counting candidates would report BL6018 for every failed call.
            return new ProbeOutcome(
                null,
                errors.Contains(AmbiguousCallDiagnosticId),
                errors.Contains(StaticThroughInstanceDiagnosticId));
        }

        /// <summary>
        /// The distinct call-site keyword shapes worth probing, all-by-value FIRST.
        ///
        /// <para>BasicLang writes no keyword at a call site, so the shape has to be derived from the
        /// candidates. Ordering matters and is not cosmetic: all-by-value first means that when a
        /// by-value and a by-ref overload could both take the call, the by-value one wins — which
        /// is BasicLang's <c>ByVal</c> default. The remainder are ordered by their own text so the
        /// answer never depends on metadata-table order.</para>
        ///
        /// <para><c>in</c> and <c>ref readonly</c> contribute no shape: C# accepts a by-value
        /// argument for both, so the all-by-value shape already covers them.</para>
        ///
        /// <para>A NULL-literal position never takes a keyword (<c>out null</c> is not C#), so
        /// a candidate's keyword there is forced to by-value — the candidate could never bind
        /// that slot by ref anyway.</para>
        /// </summary>
        private static IReadOnlyList<string[]> RefKindKeywordMasks(
            IEnumerable<(ISymbol Symbol, NetMemberDescriptor Descriptor)> candidates,
            IReadOnlyList<string> arguments)
        {
            var argumentCount = arguments.Count;
            var byValue = new string[argumentCount];
            for (var i = 0; i < argumentCount; i++)
                byValue[i] = string.Empty;

            var distinct = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                var parameters = candidate.Descriptor.Parameters;

                // Fewer declared parameters than arguments means the call can only bind through a
                // params expansion, and an expanded element is always by-value.
                if (parameters.Count < argumentCount)
                    continue;

                var mask = new string[argumentCount];
                var anyKeyword = false;
                for (var i = 0; i < argumentCount; i++)
                {
                    // A lambda argument takes no ref/out keyword — a lambda expression is not a
                    // variable, so `ref (x) => …` is not even syntax.
                    mask[i] = IsNullSpelling(arguments[i]) || IsLambdaSpelling(arguments[i])
                        ? string.Empty
                        : CallSiteKeyword(parameters[i].RefKind);
                    anyKeyword |= mask[i].Length != 0;
                }

                if (anyKeyword)
                    distinct[string.Join("|", mask)] = mask;
            }

            var masks = new List<string[]> { byValue };
            foreach (var mask in distinct.Values)
            {
                if (masks.Count >= MaxRefKindMasks)
                    break;
                masks.Add(mask);
            }
            return masks;
        }

        private static string CallSiteKeyword(NetRefKind refKind)
        {
            switch (refKind)
            {
                case NetRefKind.Ref: return "ref ";
                case NetRefKind.Out: return "out ";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// The receiver's C# spelling, with <paramref name="typeArguments"/> substituted level by
        /// level — <c>global::Ns.Outer&lt;A&gt;.Inner&lt;B&gt;</c> — or null if the count does not
        /// match what the type and its containing types declare.
        ///
        /// <para>Built textually rather than through <see cref="INamedTypeSymbol.Construct"/>
        /// because constructing needs <see cref="ITypeSymbol"/>s for the arguments, and binding
        /// those needs a probe, which needs a receiver. <c>global::</c> makes the spelling immune to
        /// a namespace in a referenced assembly shadowing a leading segment.</para>
        /// </summary>
        private static string ReceiverSyntax(INamedTypeSymbol type, IReadOnlyList<string> typeArguments)
        {
            var nesting = new List<INamedTypeSymbol>();
            for (var current = type; current != null; current = current.ContainingType)
                nesting.Insert(0, current);

            if (nesting.Sum(t => t.Arity) != typeArguments.Count)
                return null;

            var text = new System.Text.StringBuilder("global::");
            var containingNamespace = type.ContainingNamespace;
            if (containingNamespace != null && !containingNamespace.IsGlobalNamespace)
                text.Append(containingNamespace.ToDisplayString()).Append('.');

            var next = 0;
            for (var level = 0; level < nesting.Count; level++)
            {
                if (level > 0)
                    text.Append('.');
                text.Append(nesting[level].Name);
                if (nesting[level].Arity == 0)
                    continue;

                text.Append('<');
                for (var i = 0; i < nesting[level].Arity; i++)
                {
                    if (i > 0)
                        text.Append(", ");
                    text.Append(typeArguments[next++]);
                }
                text.Append('>');
            }

            return text.ToString();
        }

        /// <summary>
        /// True when <paramref name="spelling"/> is exactly one complete, diagnostic-free C# type
        /// name. The guard that stops user text from becoming extra code in the probe: the parse
        /// must consume the WHOLE string and reproduce it verbatim, so
        /// <c>"System.Int32) ; static void Boom("</c> — which parses as <c>System.Int32</c> plus
        /// trailing garbage — is rejected rather than truncated and accepted.
        /// </summary>
        private static bool IsOneWellFormedTypeName(string spelling)
        {
            if (string.IsNullOrWhiteSpace(spelling))
                return false;

            var parsed = SyntaxFactory.ParseTypeName(spelling);
            return !parsed.ContainsDiagnostics
                   && parsed.FullSpan.Length == spelling.Length
                   && string.Equals(parsed.ToString(), spelling, StringComparison.Ordinal);
        }
    }
}
