using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BasicLang.Net
{
    /// <summary>
    /// The compiler's .NET type knowledge (spec §6.1), backed by a Roslyn
    /// <see cref="CSharpCompilation"/> over the reference closure
    /// <see cref="NetReferenceResolver"/> produces. The data model lives in
    /// <c>NetTypeDescriptors.cs</c>.
    ///
    /// <para><b>Why this exists.</b> Before P2a-1 the analyzer accepted any PascalCase identifier
    /// as a .NET type, so <c>New Regex(1, 2, 3)</c> type-checked clean and failed later inside
    /// <c>csc</c> with a message about generated code the user never wrote. This is the first
    /// component that can answer "does that type exist" and "does that member exist" truthfully.
    /// </para>
    ///
    /// <para><b>Why Roslyn and not reflection.</b> <c>MetadataReference.CreateFromFile</c> READS
    /// metadata; it does not load assemblies into the process. No file locks, no module
    /// initializers, no unload problem — verified by
    /// <c>NetTypeResolverTests.ResolvingFromAnOnDiskAssemblyDoesNotLoadThatAssembly</c>. That is a
    /// correctness improvement over <c>TypeRegistry</c>'s <c>Assembly.LoadFrom</c> (spec §6.2),
    /// not merely a convenience: the LSP could not see a rebuilt referenced assembly without
    /// restarting. Roslyn is also the only thing that can do §6.1's third job, overload resolution
    /// with generics, inheritance, optional parameters, <c>params</c> and implicit conversions —
    /// which Task 5 adds on top of this.</para>
    ///
    /// <para><b>Ownership: build ONE per reference closure and keep it.</b> Construction is not
    /// free — measured at 209 ms cold and a steady 46–49 ms per fresh instance over a 168-assembly
    /// framework closure, since every reference is opened and every lookup cache starts empty.
    /// Nothing in this API stops a caller from constructing one per call, so the guidance has to
    /// live here: a consumer on the IntelliSense path that builds one per debounced pass pays
    /// ~47 ms and throws away its warm cache on every keystroke. Rebuild only when the closure
    /// itself changes.</para>
    ///
    /// <para><b>Not thread-safe to CONSTRUCT concurrently, safe to USE concurrently.</b> Roslyn's
    /// symbol APIs are thread-safe and the lookup cache is a
    /// <see cref="ConcurrentDictionary{TKey, TValue}"/>, which matters because spec §6.2 makes the
    /// LSP one of three consumers and Task 7 points it here.</para>
    ///
    /// <para><b>Nothing consumes this yet.</b> P2a-1 changes the behavior of not one existing
    /// program; Task 8 wires the resolver into the analyzer warning-only.</para>
    /// </summary>
    internal sealed class NetTypeResolver
    {
        /// <summary>
        /// Fallback spelling for symbols <see cref="TypeName"/> does not special-case (type
        /// parameters, <c>dynamic</c>, function pointers). Omits <c>global::</c> and does NOT set
        /// <c>UseSpecialTypes</c>, so <c>System.Int32</c> rather than <c>int</c>.
        /// </summary>
        private static readonly SymbolDisplayFormat FullNameFormat = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

        private readonly CSharpCompilation _compilation;

        /// <summary>
        /// The referenced assemblies, resolved once. Only used on the MISS path, to tell absence
        /// from ambiguity by counting how many assemblies DECLARE a name.
        /// </summary>
        private readonly IReadOnlyList<IAssemblySymbol> _assemblies;

        private readonly ImmutableArray<NetReferenceDiagnostic> _diagnostics;

        /// <summary>
        /// Lookup cache. The analyzer resolves the same handful of names once per reference in a
        /// file, and the miss path scans every referenced assembly — worth memoizing, and it also
        /// makes repeated lookups order-independent in cost.
        ///
        /// <para><b>Concurrent by requirement, not by caution.</b> Spec §6.2 makes the LSP one of
        /// this resolver's three consumers, and Task 7 replaces the LSP's <c>Assembly.LoadFrom</c>
        /// path with it — so one instance will be shared across concurrent LSP requests. A plain
        /// <see cref="Dictionary{TKey, TValue}"/> mutated from two request threads corrupts its
        /// buckets and hangs or throws at some unrelated later read, which is about the worst
        /// failure shape available for a language server.</para>
        ///
        /// <para><b>Unbounded, and keyed on arbitrary user text.</b> On the IntelliSense path every
        /// half-typed identifier becomes a permanent entry. Entries are small (an outcome plus a
        /// symbol reference Roslyn already holds), and the resolver's lifetime is the closure's, so
        /// this is bounded in practice by how much a user types between reference changes — but it
        /// is a real growth path and Task 7/8 should decide whether to cap it.</para>
        /// </summary>
        private readonly ConcurrentDictionary<string, CachedLookup> _cache =
            new ConcurrentDictionary<string, CachedLookup>(StringComparer.Ordinal);

        private readonly struct CachedLookup
        {
            public CachedLookup(NetTypeLookupOutcome outcome, INamedTypeSymbol symbol)
            {
                Outcome = outcome;
                Symbol = symbol;
            }

            public NetTypeLookupOutcome Outcome { get; }

            /// <summary>
            /// Kept alongside the public result so Task 5's overload resolution — which needs the
            /// real <see cref="IMethodSymbol"/>s — does not have to repeat the lookup.
            /// </summary>
            public INamedTypeSymbol Symbol { get; }
        }

        private NetTypeResolver(
            CSharpCompilation compilation,
            IReadOnlyList<IAssemblySymbol> assemblies,
            ImmutableArray<NetReferenceDiagnostic> diagnostics)
        {
            _compilation = compilation;
            _assemblies = assemblies;
            _diagnostics = diagnostics;
        }

        /// <summary>
        /// Builds a resolver over <paramref name="assemblyPaths"/> — normally
        /// <see cref="NetReferenceClosure.All"/>.
        ///
        /// <para><b>Unreadable references are skipped, never thrown.</b> Every input here is
        /// reachable from user text: a <c>&lt;HintPath&gt;</c> can name a native DLL, a truncated
        /// file, or something deleted between resolution and use.
        /// <c>MetadataReference.CreateFromFile</c> throws <c>FileNotFoundException</c> for the
        /// missing case and DEFERS <c>BadImageFormatException</c> for the malformed one, so an
        /// unguarded resolver either crashes a build or degrades silently — and this runs on the
        /// IntelliSense path too. Each skip becomes a BL6021 (§11.4: "reference could not be
        /// resolved"), which is the same code <see cref="NetReferenceResolver"/> uses and the
        /// opposite of the silent drop it replaced.</para>
        /// </summary>
        public static NetTypeResolver Create(IEnumerable<string> assemblyPaths)
        {
            var diagnostics = new List<NetReferenceDiagnostic>();
            var references = new List<PortableExecutableReference>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in assemblyPaths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
                    continue;   // duplicate metadata references are a Roslyn error; the closure is
                                // already de-duplicated, this only keeps other callers safe

                try
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
                catch (Exception ex) when (ex is IOException || ex is BadImageFormatException
                                           || ex is ArgumentException || ex is NotSupportedException
                                           || ex is System.Security.SecurityException
                                           || ex is UnauthorizedAccessException)
                {
                    diagnostics.Add(Unreadable(path, ex.Message));
                }
            }

            var compilation = CSharpCompilation.Create("blnet.resolver", references: references);

            // A malformed or native DLL creates a MetadataReference happily and only fails when
            // Roslyn tries to make a symbol out of it — at which point it is a null symbol, not an
            // exception. Asking now costs ~0ms over a full framework closure and converts a
            // deferred silent degradation into a diagnostic at construction.
            var assemblies = new List<IAssemblySymbol>();
            foreach (var reference in references)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
                    assemblies.Add(assembly);
                else
                    diagnostics.Add(Unreadable(reference.FilePath,
                        "the file is not a managed assembly Roslyn can read metadata from"));
            }

            return new NetTypeResolver(compilation, assemblies, diagnostics.ToImmutableArray());
        }

        private static NetReferenceDiagnostic Unreadable(string path, string reason) =>
            new NetReferenceDiagnostic("BL6021",
                $"Reference '{path}' could not be read as .NET metadata and will be ignored for "
                + $".NET type resolution: {reason}",
                // A WARNING: the reference may be irrelevant to everything this program does, and
                // P2a-1 is warning-only throughout. Task 8/13 decide how this surfaces.
                IsWarning: true);

        /// <summary>
        /// References that could not be read, one BL6021 each. Empty for a well-formed closure —
        /// <see cref="NetReferenceResolver"/>'s framework set is already filtered to managed
        /// assemblies precisely so that Roslyn never sees <c>coreclr.dll</c>. Immutable: fixed at
        /// construction and never appended to.
        /// </summary>
        public IReadOnlyList<NetReferenceDiagnostic> Diagnostics => _diagnostics;

        /// <summary>
        /// The type named <paramref name="fullName"/>, or null if it does not exist OR is
        /// ambiguous. Use <see cref="ResolveTypeDetailed"/> when the two need different
        /// diagnostics.
        ///
        /// <para><b>Accepted spellings.</b> A metadata name is accepted verbatim
        /// (<c>Outer+Inner</c>, <c>List`1</c>), and a nested type may also be spelled with dots
        /// (<c>System.Environment.SpecialFolder</c>) because BasicLang source has no <c>+</c>
        /// syntax and can produce no other form.</para>
        ///
        /// <para><b>Generic arity is required and never guessed.</b>
        /// <c>System.Collections.Generic.List`1</c> resolves;
        /// <c>System.Collections.Generic.List</c> is <see cref="NetTypeLookupOutcome.NotFound"/> BY
        /// DESIGN. Arity is part of a generic type's identity — <c>System.Func</c> exists only as
        /// <c>Func`1</c>…<c>Func`17</c>, so there is no arity to fall back to and guessing one
        /// would fabricate a binding, which is the precise failure this class exists to remove.
        /// Every caller that can reach here parsed <c>List(Of Integer)</c> and therefore knows the
        /// arity. (Consequence: <see cref="NetTypeDescriptor.FullName"/> does not round-trip through
        /// this method for a generic type — it reports <c>List&lt;T&gt;</c>, the readable form,
        /// because its consumers are diagnostics and the mangler, not this lookup.)</para>
        ///
        /// <para><b><see cref="NetTypeDescriptor.IsPublic"/> is EFFECTIVE accessibility.</b> A
        /// public type nested inside an internal one reports false, because a shim that references
        /// it fails in <c>csc</c> with CS0122 — the late-failure shape P2a-1 exists to remove. The
        /// type still resolves; the caller decides the diagnostic.</para>
        /// </summary>
        public NetTypeDescriptor ResolveType(string fullName) => ResolveTypeDetailed(fullName).Type;

        /// <summary>
        /// <see cref="ResolveType"/> plus the reason. See
        /// <see cref="NetTypeLookupOutcome"/> for why the reason has to survive.
        /// </summary>
        public NetTypeLookupResult ResolveTypeDetailed(string fullName)
        {
            var lookup = Lookup(fullName);
            return new NetTypeLookupResult(lookup.Outcome, Describe(lookup.Symbol));
        }

        /// <summary>
        /// Every public constructor, method, property and field declared on the named type or on
        /// its base types (spec §7.2).
        ///
        /// <para><b>Empty — not an exception — when the type does not resolve</b>, which covers
        /// BOTH <see cref="NetTypeLookupOutcome.NotFound"/> and
        /// <see cref="NetTypeLookupOutcome.Ambiguous"/>. Those are different diagnostics (BL6016 vs
        /// BL6023), so a caller that needs to tell "you typo'd the name" from "that name is
        /// declared in two references" must ask <see cref="ResolveTypeDetailed"/> first rather than
        /// infer anything from an empty member list. Throwing is not an option: this is called from
        /// the analyzer and the IntelliSense path, where a user typo must not become a crashed
        /// build or a dead LSP request.</para>
        ///
        /// <para><b>The base walk stops at (and excludes) <see cref="object"/></b>, per §7.2's
        /// "excluding <c>System.Object</c>'s members unless overridden": an override is a member of
        /// the overriding type and so is included anyway, while <c>ReferenceEquals</c> and friends
        /// are noise that would cost a shim export apiece. Intermediate bases —
        /// <c>System.ValueType</c>, <c>System.Enum</c>, <c>System.MarshalByRefObject</c> — are
        /// included, because "base types" is what the spec says.</para>
        ///
        /// <para><b>CONSTRUCTORS COME ONLY FROM THE QUERIED TYPE.</b> Constructors are not
        /// inherited — <c>New Derived(argsOfABaseCtor)</c> is a compile error unless
        /// <c>Derived</c> declares that signature — so collecting them from base types invents
        /// members that cannot be called. Read literally §7.2 says "declared on the type and on its
        /// base types" without carving constructors out; that reading is taken as a spec slip
        /// because the alternative makes Task 5 resolve an uncallable constructor and Task 12 emit
        /// a proxy slot for it. Measured: it added a spurious member to
        /// <c>FileNotFoundException</c> and <c>ArgumentNullException</c> among many others, and
        /// silently replaced the base's identical-signature constructors elsewhere.</para>
        ///
        /// <para><b>Interfaces are not walked.</b> §7.2 says "the type and its base types". An
        /// interface member reachable on a class is a member of that class.</para>
        ///
        /// <para><b>An override appears ONCE, under its most-derived declaration</b> — see
        /// <see cref="CandidateMembers"/>, which owns the collapse.</para>
        ///
        /// <para><b>Deliberate exclusions, all with the same reason — they are not callable
        /// members of the surface:</b> property/event accessors (<c>get_X</c>/<c>set_X</c>: a
        /// property is ONE member, or every property costs three exports and three proxy slots),
        /// events, nested types (resolve them by name instead), static constructors, finalizers,
        /// and user-defined operators and conversions. Non-public members are excluded because the
        /// shim cannot call them.</para>
        ///
        /// <para><b>Implicitly-declared members are excluded, and for metadata that means exactly
        /// one thing:</b> the synthesized public parameterless constructor Roslyn gives every
        /// metadata VALUE type. Probed across <c>DateTime</c>, <c>Decimal</c>, <c>Guid</c>,
        /// <c>DayOfWeek</c> and <c>FileMode</c> — each has exactly one implicitly-declared member
        /// and it is always that <c>.ctor</c>; classes (<c>StringBuilder</c>, <c>FileStream</c>,
        /// <c>Regex</c>) have none. It is NOT about an enum's <c>value__</c> field: Roslyn's PE
        /// symbols never surface that at all, so do not credit this check with removing it.
        /// Consequence worth knowing before Task 5: <c>New SomeStruct()</c> has no member here to
        /// resolve against, which is correct in that there is no metadata token to call — a
        /// zero-argument value-type construction is <c>default(T)</c>, not a call.</para>
        /// </summary>
        public IReadOnlyList<NetMemberDescriptor> GetMembers(string fullName) =>
            CandidateMembers(fullName).Select(m => m.Descriptor).ToList();

        /// <summary>
        /// <see cref="GetMembers"/>, but keeping each member's Roslyn symbol alongside its
        /// descriptor.
        ///
        /// <para><b>INTERNAL ON PURPOSE — this is Task 5's seam, not part of the facade.</b>
        /// Overload resolution needs real <see cref="IMethodSymbol"/>s, and the alternative to this
        /// seam is Task 5 re-walking derived-to-base itself, which would re-meet the
        /// duplicate-override problem in a second place and put a Roslyn dependency in front of the
        /// analyzer. The walk, the exclusion rules and the duplicate collapse stay in exactly one
        /// method; Roslyn stays behind the facade.</para>
        ///
        /// <para>Ordering is derived-to-base and is RELIED ON: first-seen wins the collapse, so the
        /// most-derived declaration — the one that actually runs — is the one kept.</para>
        /// </summary>
        internal IEnumerable<(ISymbol Symbol, NetMemberDescriptor Descriptor)> CandidateMembers(string fullName)
        {
            var symbol = Lookup(fullName).Symbol;
            if (symbol == null)
                yield break;

            var seen = new HashSet<(NetMemberCategory, string, bool, int, string)>();
            var isQueriedType = true;
            for (var type = symbol;
                 type != null && type.SpecialType != SpecialType.System_Object;
                 type = type.BaseType)
            {
                foreach (var member in type.GetMembers())
                {
                    // Constructors are not inherited. See GetMembers' remarks.
                    if (!isQueriedType && member is IMethodSymbol ctor
                        && ctor.MethodKind == MethodKind.Constructor)
                        continue;

                    var described = DescribeMember(member);
                    if (described == null)
                        continue;

                    // Signature identity, NOT declaring type: an override and the member it
                    // overrides differ only in the latter, which is exactly why they collide.
                    // Every other axis of a CLR signature has to be in here or the collapse
                    // DELETES a real overload — see NetMemberDescriptor's remarks for the two
                    // (arity, ref-kind) that are invisible in a bare name-plus-types key and the
                    // 186 framework members they cost.
                    if (!seen.Add(SignatureKey(described)))
                        continue;

                    yield return (member, described);
                }
                isQueriedType = false;
            }
        }

        private static (NetMemberCategory, string, bool, int, string) SignatureKey(NetMemberDescriptor m) =>
            (m.Kind, m.Name, m.IsStatic, m.Arity, string.Join(",", m.Parameters));

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
        /// Overload results, keyed on the whole request. Separate from <see cref="_cache"/> because
        /// the key is a request, not a type name. Concurrent for the reason <see cref="_cache"/>
        /// documents, and unbounded in the same way — one entry per distinct call site.
        /// </summary>
        private readonly ConcurrentDictionary<string, NetOverloadResult> _overloadCache =
            new ConcurrentDictionary<string, NetOverloadResult>(StringComparer.Ordinal);

        private static readonly NetOverloadResult NoOverloadMatch =
            new NetOverloadResult(NetOverloadOutcome.NoMatch, null);

        private static readonly NetOverloadResult AmbiguousOverload =
            new NetOverloadResult(NetOverloadOutcome.Ambiguous, null);

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
        /// <para><b>Not the type-existence question.</b> An unknown OR ambiguous
        /// <paramref name="typeFullName"/> answers <see cref="NetOverloadOutcome.NoMatch"/>, the
        /// same contract <see cref="GetMembers"/> documents: those are BL6016 and BL6023, and §6.5
        /// says outright that "BL6018 covers ambiguous <i>overloads</i> only". Ask
        /// <see cref="ResolveTypeDetailed"/> first.</para>
        ///
        /// <para><b>There is no spelling for "I do not know this argument's type."</b> A null, empty
        /// or unresolvable argument spelling answers <see cref="NetOverloadOutcome.NoMatch"/>. A
        /// wildcard was considered and rejected: it can only widen the candidate set, so it either
        /// fabricates a binding — the exact defect this class exists to remove — or turns a valid
        /// call into a spurious BL6018. A caller that cannot type every argument must not ask.</para>
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

            // / separate the fields;  stands in for a null spelling so that it
            // cannot collide with the empty string. None of the three occurs in a type name.
            var key = string.Join("",
                new[]
                {
                    form.ToString(),
                    typeFullName ?? "",
                    memberName ?? "",
                    string.Join("", typeArguments.Select(a => a ?? "")),
                    string.Join("", arguments.Select(a => a ?? "")),
                });

            // NOTE FOR ANYONE EDITING THE THREE SEPARATORS ABOVE: they are the literal control
            // characters U+0001 (between the request's fields), U+0002 (between list elements) and
            // U+0003 (standing in for a null spelling, so a null cannot key the same entry as an
            // empty string). Most editors render them as nothing at all, so the string literals
            // above look empty -- they are not. Do not "clean them up" into "" or ",": none of the
            // three can occur in a type or member name, and that is exactly what makes two
            // different requests unable to collide on one cache entry.
            if (_overloadCache.TryGetValue(key, out var cached))
                return cached;

            var result = ResolveOverloadUncached(typeFullName, form, memberName, arguments, typeArguments);
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
            var type = Lookup(typeFullName).Symbol;
            if (type == null)
                return NoOverloadMatch;   // NotFound or Ambiguous: BL6016/BL6023, not BL6017/BL6018

            var isConstructor = form == NetCallForm.Constructor;
            if (!isConstructor && !SyntaxFacts.IsValidIdentifier(memberName))
                return NoOverloadMatch;

            foreach (var spelling in arguments.Concat(typeArguments))
            {
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

            var masks = RefKindKeywordMasks(candidates, arguments.Count);
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
                declared.Add(arguments[i] + " " + ProbeArgumentPrefix + i);

            var passed = string.Join(", ",
                Enumerable.Range(0, arguments.Count).Select(i => mask[i] + ProbeArgumentPrefix + i));

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
        /// </summary>
        private static IReadOnlyList<string[]> RefKindKeywordMasks(
            IEnumerable<(ISymbol Symbol, NetMemberDescriptor Descriptor)> candidates,
            int argumentCount)
        {
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
                    mask[i] = CallSiteKeyword(parameters[i].RefKind);
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

        // ------------------------------------------------------------------
        // Lookup
        // ------------------------------------------------------------------

        private CachedLookup Lookup(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return new CachedLookup(NetTypeLookupOutcome.NotFound, null);

            if (_cache.TryGetValue(fullName, out var cached))
                return cached;

            var result = LookupUncached(fullName);
            _cache[fullName] = result;
            return result;
        }

        private CachedLookup LookupUncached(string fullName)
        {
            var candidates = CandidateMetadataNames(fullName);

            // Fast path. Compilation.GetTypeByMetadataName searches every reference, resolves type
            // forwarders, and answers null on ambiguity.
            foreach (var candidate in candidates)
            {
                var symbol = TryGetTypeByMetadataName(candidate);
                if (symbol != null)
                    return new CachedLookup(NetTypeLookupOutcome.Resolved, symbol);
            }

            // Miss path only. Absence and ambiguity are indistinguishable above, so count the
            // assemblies that DECLARE the name. Per-assembly lookup does not dig through type
            // forwarders, so a facade (netstandard.dll, mscorlib.dll) forwarding to the real
            // definition contributes no extra hit and the framework set does not read as ambiguous.
            foreach (var candidate in candidates)
            {
                var declarations = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                foreach (var assembly in _assemblies)
                {
                    var symbol = TryGetTypeByMetadataName(assembly, candidate);
                    if (symbol != null)
                        declarations.Add(symbol);
                    if (declarations.Count > 1)
                        return new CachedLookup(NetTypeLookupOutcome.Ambiguous, null);
                }
            }

            return new CachedLookup(NetTypeLookupOutcome.NotFound, null);
        }

        /// <summary>
        /// The metadata names <paramref name="fullName"/> could mean, most literal first: the name
        /// itself, then progressively more of its trailing dots reinterpreted as nested-type
        /// separators (<c>a.b.c</c> → <c>a.b+c</c> → <c>a+b+c</c>). Rightmost first, because the
        /// leading segments are far more likely to be a namespace.
        /// </summary>
        private static IReadOnlyList<string> CandidateMetadataNames(string fullName)
        {
            var candidates = new List<string> { fullName };
            var chars = fullName.ToCharArray();
            for (var i = chars.Length - 1; i > 0; i--)
            {
                if (chars[i] != '.')
                    continue;
                chars[i] = '+';
                candidates.Add(new string(chars));
            }
            return candidates;
        }

        /// <summary>
        /// <c>GetTypeByMetadataName</c> rejects some inputs by throwing rather than answering
        /// null, and the input is user text.
        /// </summary>
        private INamedTypeSymbol TryGetTypeByMetadataName(string metadataName)
        {
            try { return _compilation.GetTypeByMetadataName(metadataName); }
            catch (ArgumentException) { return null; }
        }

        private static INamedTypeSymbol TryGetTypeByMetadataName(IAssemblySymbol assembly, string metadataName)
        {
            try { return assembly.GetTypeByMetadataName(metadataName); }
            catch (ArgumentException) { return null; }
        }

        // ------------------------------------------------------------------
        // Type naming
        // ------------------------------------------------------------------

        /// <summary>
        /// The canonical spelling of a type: fully qualified, no C# keyword or shorthand syntax,
        /// and structural through arrays, pointers and generic arguments.
        ///
        /// <para><b><c>ToDisplayString</c> alone does not deliver this, even with
        /// <c>UseSpecialTypes</c> off.</b> Measured with exactly the format above:</para>
        /// <list type="bullet">
        /// <item><description><c>System.IntPtr</c> renders as <c>nint</c> and
        /// <c>System.UIntPtr</c> as <c>nuint</c> — 670 and 274 occurrences respectively across the
        /// public framework surface's parameter and return spellings, plus <c>nint*</c>,
        /// <c>nint[]</c> and <c>nint?</c> forms.</description></item>
        /// <item><description>Tuples render as tuple SYNTAX INCLUDING ELEMENT NAMES —
        /// <c>Math.SinCos</c> returns <c>(System.Double Sin, System.Double Cos)</c> — so two APIs
        /// over the same runtime type get different spellings, and the string carries <c>(</c>,
        /// <c>)</c>, <c>,</c> and spaces straight into whatever Task 9 mangles.</description></item>
        /// </list>
        /// <para>Both matter twice over: as §7.3 export names, and as components of the signature
        /// key <see cref="CandidateMembers"/> collapses on.</para>
        /// </summary>
        private static string TypeName(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array)
                return TypeName(array.ElementType) + "[" + new string(',', Math.Max(0, array.Rank - 1)) + "]";

            if (type is IPointerTypeSymbol pointer)
                return TypeName(pointer.PointedAtType) + "*";

            if (type is INamedTypeSymbol named)
            {
                // A tuple is ValueTuple<...>; its element names are metadata decoration, not part
                // of the type, and must not reach a name.
                if (named.IsTupleType && named.TupleUnderlyingType != null)
                    named = named.TupleUnderlyingType;

                if (named.SpecialType == SpecialType.System_IntPtr)
                    return "System.IntPtr";
                if (named.SpecialType == SpecialType.System_UIntPtr)
                    return "System.UIntPtr";

                var name = QualifiedName(named);
                return named.TypeArguments.Length == 0
                    ? name
                    : name + "<" + string.Join(", ", named.TypeArguments.Select(TypeName)) + ">";
            }

            // Type parameters, dynamic, function pointers: nothing to normalize.
            return type.ToDisplayString(FullNameFormat);
        }

        /// <summary>Namespace + containing types + name, with no type-argument list.</summary>
        private static string QualifiedName(INamedTypeSymbol named)
        {
            var parts = new List<string>();
            for (var type = named; type != null; type = type.ContainingType)
                parts.Insert(0, type.Name);

            var containingNamespace = named.ContainingNamespace;
            if (containingNamespace != null && !containingNamespace.IsGlobalNamespace)
                parts.Insert(0, containingNamespace.ToDisplayString());

            return string.Join(".", parts);
        }

        // ------------------------------------------------------------------
        // Description
        // ------------------------------------------------------------------

        private static NetTypeDescriptor Describe(INamedTypeSymbol symbol) =>
            symbol == null
                ? null
                : new NetTypeDescriptor(TypeName(symbol), KindOf(symbol), IsEffectivelyPublic(symbol));

        /// <summary>
        /// Public all the way out. A public type nested in an internal one is NOT reachable, and
        /// reporting it as public hands the shim a reference that fails in <c>csc</c> with CS0122 —
        /// exactly the late failure P2a-1 exists to move earlier.
        /// </summary>
        private static bool IsEffectivelyPublic(INamedTypeSymbol symbol)
        {
            for (var type = symbol; type != null; type = type.ContainingType)
            {
                if (type.DeclaredAccessibility != Accessibility.Public)
                    return false;
            }
            return true;
        }

        private static NetTypeCategory KindOf(INamedTypeSymbol symbol)
        {
            switch (symbol.TypeKind)
            {
                case TypeKind.Class: return NetTypeCategory.Class;
                case TypeKind.Struct: return NetTypeCategory.Struct;
                case TypeKind.Interface: return NetTypeCategory.Interface;
                case TypeKind.Enum: return NetTypeCategory.Enum;
                case TypeKind.Delegate: return NetTypeCategory.Delegate;
                default: return NetTypeCategory.Other;
            }
        }

        /// <summary>
        /// The <see cref="NetMemberDescriptor"/> for <paramref name="member"/>, or null if it is not
        /// part of the surface. See <see cref="GetMembers"/> for the exclusion list and its
        /// rationale.
        /// </summary>
        private static NetMemberDescriptor DescribeMember(ISymbol member)
        {
            if (member.DeclaredAccessibility != Accessibility.Public || member.IsImplicitlyDeclared)
                return null;

            switch (member)
            {
                case IMethodSymbol method:
                    var kind = method.MethodKind == MethodKind.Constructor
                        ? NetMemberCategory.Constructor
                        : method.MethodKind == MethodKind.Ordinary
                            ? NetMemberCategory.Method
                            : (NetMemberCategory?)null;
                    if (kind == null)
                        return null;
                    return new NetMemberDescriptor(
                        method.MetadataName,
                        TypeName(method.ContainingType),
                        kind.Value,
                        method.IsStatic,
                        method.Arity,
                        TypeName(method.ReturnType),
                        Describe(method.Parameters));

                case IPropertySymbol property:
                    return new NetMemberDescriptor(
                        property.MetadataName,
                        TypeName(property.ContainingType),
                        NetMemberCategory.Property,
                        property.IsStatic,
                        arity: 0,
                        TypeName(property.Type),
                        // An indexer's parameters, empty for an ordinary property. Without these
                        // two indexers present one identity and the collapse eats one.
                        Describe(property.Parameters));

                case IFieldSymbol field:
                    return new NetMemberDescriptor(
                        field.MetadataName,
                        TypeName(field.ContainingType),
                        NetMemberCategory.Field,
                        field.IsStatic,
                        arity: 0,
                        TypeName(field.Type),
                        Array.Empty<NetParameterDescriptor>());

                default:
                    return null;   // events, nested types, everything else
            }
        }

        private static IReadOnlyList<NetParameterDescriptor> Describe(
            IEnumerable<IParameterSymbol> parameters) =>
            parameters
                .Select(p => new NetParameterDescriptor(RefKindOf(p.RefKind), TypeName(p.Type)))
                .ToList();

        /// <summary>
        /// Roslyn's <see cref="RefKind"/> mapped onto ours. The default arm covers
        /// <c>RefKind.RefReadOnlyParameter</c> (C# 12's <c>ref readonly</c>) and anything a later
        /// Roslyn adds — mapping an unknown ref-kind onto <see cref="NetRefKind.None"/> instead
        /// would silently merge it with by-value and delete an overload.
        /// </summary>
        private static NetRefKind RefKindOf(RefKind refKind)
        {
            switch (refKind)
            {
                case RefKind.None: return NetRefKind.None;
                case RefKind.Ref: return NetRefKind.Ref;
                case RefKind.Out: return NetRefKind.Out;
                case RefKind.In: return NetRefKind.In;
                default: return NetRefKind.RefReadOnly;
            }
        }
    }
}
