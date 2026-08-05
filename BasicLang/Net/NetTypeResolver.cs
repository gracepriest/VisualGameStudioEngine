using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
    internal sealed partial class NetTypeResolver
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
        /// <para><b>Keyed on arbitrary user text, and therefore capped</b> — see
        /// <see cref="MaxTypeCacheEntries"/> for the cap and for why it is far higher than
        /// <see cref="MaxOverloadCacheEntries"/>.</para>
        /// </summary>
        private readonly ConcurrentDictionary<string, CachedLookup> _cache =
            new ConcurrentDictionary<string, CachedLookup>(StringComparer.Ordinal);

        /// <summary>
        /// Cap on <see cref="_cache"/>. Task 7 gave this resolver its first real owner
        /// (<c>TypeRegistry</c>, whose instance lives as long as the LSP's document manager), so
        /// "unbounded but short-lived" stopped being true and the growth path had to be closed.
        ///
        /// <para><b>Why an order of magnitude above
        /// <see cref="MaxOverloadCacheEntries"/>.</b> Growth here is LINEAR in the distinct type
        /// spellings a user types, not combinatorial: the key is one type name, and an entry is an
        /// enum plus a symbol reference the compilation already holds — tens of bytes, against an
        /// overload entry's whole descriptor and parameter list. The recompute cost also runs the
        /// other way: a miss scans every referenced assembly at ~17 ms, against ~2 ms for an
        /// overload probe, so clearing this cache is the more expensive mistake. High cap, purely as
        /// a backstop against a session that types forever.</para>
        ///
        /// <para><b>Deliberately out of reach of the test suite.</b> Constraint (c) measured during
        /// Task 5: a mutation that merely reduced cache HITS caused one extra Roslyn bind, which
        /// lazily loaded a Roslyn assembly and tripped
        /// <c>NetTypeResolverTests.DoesNotLoadAssembliesIntoTheProcess</c>'s before/after assembly
        /// count. A cap low enough for ordinary fixtures to reach would make that test's outcome
        /// depend on how many lookups ran before it.</para>
        /// </summary>
        internal const int MaxTypeCacheEntries = 32768;

        /// <summary>Live entry count, for the bound's test. Not part of the facade.</summary>
        internal int TypeCacheCount => _cache.Count;

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

        /// <summary>
        /// The referenced assemblies keyed by the path they were read from, so a consumer that
        /// thinks in FILES rather than in type names can ask what one particular assembly declares.
        /// <c>TypeRegistry</c> is that consumer: its job is "index every namespace this DLL exports"
        /// and "load every type this DLL exports", which is a per-file question the by-name lookup
        /// cannot answer. Case-insensitive because the keys are Windows paths.
        /// </summary>
        private readonly IReadOnlyDictionary<string, IAssemblySymbol> _assembliesByPath;

        private NetTypeResolver(
            CSharpCompilation compilation,
            IReadOnlyList<IAssemblySymbol> assemblies,
            IReadOnlyDictionary<string, IAssemblySymbol> assembliesByPath,
            ImmutableArray<NetReferenceDiagnostic> diagnostics)
        {
            _compilation = compilation;
            _assemblies = assemblies;
            _assembliesByPath = assembliesByPath;
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
            var byPath = new Dictionary<string, IAssemblySymbol>(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in references)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
                {
                    assemblies.Add(assembly);
                    if (reference.FilePath != null)
                        byPath[reference.FilePath] = assembly;
                }
                else
                {
                    diagnostics.Add(Unreadable(reference.FilePath,
                        "the file is not a managed assembly Roslyn can read metadata from"));
                }
            }

            return new NetTypeResolver(compilation, assemblies, byPath, diagnostics.ToImmutableArray());
        }

        private static NetReferenceDiagnostic Unreadable(string path, string reason) =>
            new NetReferenceDiagnostic("BL6021",
                $"Reference '{path}' could not be read as .NET metadata and will be ignored for "
                + $".NET type resolution: {reason}",
                // Deliberately still a WARNING after the P2a-2 flip promoted the other
                // BL6021s: this fires for a file that RESOLVED on disk but is not readable
                // managed metadata (a native DLL beside the exe is the common case), and the
                // program may never name a type from it — resolution degrades gracefully to
                // "contributes no types", and anything the program actually needed then draws
                // its own hard BL6016 at the use site. Erroring here would fail builds over
                // references they never touch.
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
        /// (<c>Outer+Inner</c>, <c>List`1</c>), a nested type may also be spelled with dots
        /// (<c>System.Environment.SpecialFolder</c>) because BasicLang source has no <c>+</c>
        /// syntax and can produce no other form, and — since P2a-2 Task 8b — this class's OWN
        /// C# spelling is accepted too (<c>List&lt;System.Int32&gt;</c>,
        /// <c>List&lt;T&gt;.Enumerator</c>), because <see cref="TypeName"/> hands that form to
        /// callers who then look it up again. See <see cref="CandidateMetadataNames"/>.</para>
        ///
        /// <para><b>Generic arity is required and never guessed.</b>
        /// <c>System.Collections.Generic.List`1</c> resolves;
        /// <c>System.Collections.Generic.List</c> is <see cref="NetTypeLookupOutcome.NotFound"/> BY
        /// DESIGN. Arity is part of a generic type's identity — <c>System.Func</c> exists only as
        /// <c>Func`1</c>…<c>Func`17</c>, so there is no arity to fall back to and guessing one
        /// would fabricate a binding, which is the precise failure this class exists to remove.
        /// Every caller that can reach here parsed <c>List(Of Integer)</c> and therefore knows the
        /// arity. A C# spelling states its arity through the argument COUNT, so
        /// <c>List&lt;System.Int32&gt;</c> is not a guess — but the count is all that survives the
        /// translation, so a CONSTRUCTED generic resolves to its DEFINITION
        /// (<see cref="NetTypeDescriptor.FullName"/> then reports <c>List&lt;T&gt;</c>, not
        /// <c>List&lt;System.Int32&gt;</c>). Everything this class answers about a type — existence,
        /// accessibility, kind, members — is a property of the definition.</para>
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

            // ---- D-P1 (spec §14.15, P2a-2 Task 4 Step 2a): the two-name System.Object
            // allowlist. The walk above deliberately stops BEFORE System.Object (§7.2); exactly
            // two of Object's members are admitted anyway — ToString() and GetHashCode(), both
            // nullary, both §8.3-marshalable (String / Int32, no Object anywhere in either
            // signature) — because they are callable on EVERY .NET object and a surface without
            // them turns `x.ToString()` into a BL6017 on a valid program. This is NOT a general
            // "any marshalable nullary Object member" rule: GetType() stays excluded (the
            // reflection root) and Equals(Object) stays excluded (§8.3 — Object is permanently
            // Rejected). The `seen` collapse keeps an OVERRIDE authoritative: a type that
            // overrides ToString already yielded it above under its most-derived declaration, so
            // the System.Object entry collides on signature identity and is dropped here
            // (StringBuilder.ToString() reports System.Text.StringBuilder, never System.Object).
            // Value types get the same answer through a different door — System.ValueType's
            // overrides are ordinary walk output — and static classes are skipped outright: an
            // instance member cannot be called through a type name, so admitting one would put an
            // uncallable member into a §7.2 declared surface.
            if (!symbol.IsStatic)
            {
                var objectType = _compilation.GetSpecialType(SpecialType.System_Object);
                foreach (var allowed in ObjectAllowlistMemberNames)
                {
                    foreach (var member in objectType.GetMembers(allowed))
                    {
                        if (member is not IMethodSymbol { Parameters.Length: 0, IsStatic: false })
                            continue;

                        var described = DescribeMember(member);
                        if (described == null)
                            continue;

                        if (!seen.Add(SignatureKey(described)))
                            continue;

                        yield return (member, described);
                    }
                }
            }
        }

        /// <summary>
        /// The D-P1 two-name allowlist (spec §14.15), THE single source — exactly these two;
        /// see <see cref="CandidateMembers"/> for why the list must not grow. INTERNAL so the
        /// analyzer's <c>ObjectMemberNames</c> lift (<c>SemanticAnalyzer.NetObjectAllowlistNames</c>)
        /// derives from it instead of hand-copying the pair.
        /// </summary>
        internal static readonly string[] ObjectAllowlistMemberNames = { "ToString", "GetHashCode" };

        /// <summary>
        /// The resolved type's Roslyn symbol, or null (NotFound and Ambiguous alike — ask
        /// <see cref="ResolveTypeDetailed"/> which). INTERNAL SEAM for
        /// <c>NetSurfaceCollector</c>'s declared-type attribute checks (a queried type's own
        /// AOT-hostility must cover its whole surface, including the D-P1 allowlist members
        /// whose DECLARING type is <c>System.Object</c>); not part of the facade.
        /// </summary>
        internal INamedTypeSymbol TypeSymbol(string fullName) => Lookup(fullName).Symbol;

        /// <summary>
        /// True when <paramref name="fullName"/> resolves to a <c>ref struct</c> —
        /// <c>Span&lt;T&gt;</c>, <c>ReadOnlySpan&lt;T&gt;</c>,
        /// <c>Regex.ValueMatchEnumerator</c> and friends.
        ///
        /// <para>Spec §8.3 makes these NOT MARSHALABLE, and the reason is structural rather
        /// than a gap to be filled later: every non-<c>ref</c> value type crosses as a BOXED
        /// handle, and a ref-like type cannot be boxed at all —
        /// <c>GCHandle.Alloc(object)</c> is the operation P0's handle table is built on and it
        /// has nothing to take. The surface collector already refuses them structurally
        /// (<c>FirstUnmarshalable</c> checks <c>IsRefLikeType</c>); this seam is for the
        /// analyzer, which needs the same answer from a NAME so a resolved CALL SITE gets a
        /// positioned BL6019 instead of a positionless codegen refusal.</para>
        /// </summary>
        internal bool IsRefLikeType(string fullName) => Lookup(fullName).Symbol?.IsRefLikeType == true;

        /// <summary>
        /// The metadata full name of an ENUM's underlying integral type
        /// (<c>System.Int32</c> for <c>System.IO.FileMode</c>), or null when
        /// <paramref name="fullName"/> does not resolve or is not an enum.
        ///
        /// <para>Spec §8.3's "enums → underlying integral" row. This is the ONE piece of
        /// information that row needs and that neither emitter can recover, because both see
        /// only a type NAME — which is exactly why an enum-typed parameter had no wire form
        /// and was refused. Answered from Roslyn here and carried on the descriptor.</para>
        /// </summary>
        internal string EnumUnderlyingTypeFullName(string fullName)
        {
            var symbol = Lookup(fullName).Symbol;
            return symbol is { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying }
                ? TypeName(underlying)
                : null;
        }

        /// <summary>
        /// True when <paramref name="fullName"/> resolves and derives from
        /// <c>System.Exception</c> (or IS it). Spec §11.1's ladder-trigger completion (P2a-2
        /// Task 4): a catch clause whose type resolves as a .NET exception gets a
        /// <c>NetException</c> ladder arm carrying the resolver-supplied fully-qualified name;
        /// gating on exception-ness keeps a resolved NON-exception catch type from acquiring an
        /// arm that could never match a managed chain.
        /// </summary>
        internal bool IsExceptionType(string fullName)
        {
            for (var type = Lookup(fullName).Symbol; type != null; type = type.BaseType)
            {
                if (string.Equals(type.Name, "Exception", StringComparison.Ordinal)
                    && type.ContainingType == null
                    && string.Equals(type.ContainingNamespace?.ToDisplayString(), "System",
                                     StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static (NetMemberCategory, string, bool, int, string) SignatureKey(NetMemberDescriptor m) =>
            (m.Kind, m.Name, m.IsStatic, m.Arity, string.Join(",", m.Parameters));

        // ------------------------------------------------------------------
        // Per-assembly enumeration (Task 7's seam for TypeRegistry)
        // ------------------------------------------------------------------

        /// <summary>
        /// True when <paramref name="assemblyPath"/> was among the paths this resolver was built
        /// over AND its metadata was readable. A caller that needs a path this resolver does not
        /// cover has to build a resolver that does — the reference set is fixed at construction.
        /// </summary>
        internal bool Covers(string assemblyPath) =>
            assemblyPath != null && _assembliesByPath.ContainsKey(assemblyPath);

        /// <summary>
        /// Every PUBLIC type the assembly at <paramref name="assemblyPath"/> declares, nested types
        /// included and flattened — the metadata-reading equivalent of
        /// <c>Assembly.GetExportedTypes()</c>, which is what <c>TypeRegistry</c> called through
        /// <c>Assembly.LoadFrom</c> before Task 7 (spec §6.2). Empty when the path is not
        /// <see cref="Covers"/>ed, never a throw: the caller is on the IntelliSense path.
        ///
        /// <para><b>Declared, not exported-and-forwarded.</b> Reflection's
        /// <c>GetExportedTypes()</c> on a FACADE assembly follows its type forwarders and reports the
        /// targets; a metadata walk reports only what the file itself declares, so a pure facade
        /// (<c>netstandard.dll</c>, <c>mscorlib.dll</c>, the <c>System.*</c> shims) contributes
        /// nothing here. That is the right answer for both of <c>TypeRegistry</c>'s uses: the type is
        /// still reached through the assembly that really declares it, and attributing it to the
        /// facade as well only duplicated it in the namespace index under a second path.</para>
        ///
        /// <para><b>Nesting is flattened, matching what it replaces.</b> A public type nested inside
        /// a public type is yielded as its own entry, because that is how
        /// <c>GetExportedTypes()</c> reported it and how <c>TypeRegistry</c> indexed it —
        /// <c>System.Environment+SpecialFolder</c> is a name the LSP looks up. A type nested inside a
        /// NON-public type is not reachable and is not yielded, which
        /// <see cref="Accessibility.Public"/> at every level gives for free.</para>
        /// </summary>
        internal IEnumerable<INamedTypeSymbol> PublicTypesIn(string assemblyPath)
        {
            if (assemblyPath == null || !_assembliesByPath.TryGetValue(assemblyPath, out var assembly))
                return Array.Empty<INamedTypeSymbol>();

            return PublicTypesInNamespace(assembly.GlobalNamespace);
        }

        private static IEnumerable<INamedTypeSymbol> PublicTypesInNamespace(INamespaceSymbol ns)
        {
            foreach (var member in ns.GetMembers())
            {
                if (member is INamespaceSymbol child)
                {
                    foreach (var type in PublicTypesInNamespace(child))
                        yield return type;
                }
                else if (member is INamedTypeSymbol type)
                {
                    foreach (var nested in PublicTypeAndNested(type))
                        yield return nested;
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> PublicTypeAndNested(INamedTypeSymbol type)
        {
            // Stopping the recursion here rather than filtering afterwards is what makes a type
            // nested in a non-public type unreachable: accessibility does not widen on the way down.
            if (type.DeclaredAccessibility != Accessibility.Public)
                yield break;

            yield return type;

            foreach (var nested in type.GetTypeMembers())
            {
                foreach (var deeper in PublicTypeAndNested(nested))
                    yield return deeper;
            }
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

            // Bounded for the reason MaxTypeCacheEntries gives; cleared wholesale for the reason
            // MaxOverloadCacheEntries gives.
            if (_cache.Count >= MaxTypeCacheEntries)
                _cache.Clear();

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
        ///
        /// <para><b>Then the same ladder again over the C#-generic spelling's metadata form</b>
        /// (P2a-2 Task 8b), so that a name <see cref="TypeName"/> PRODUCES is a name this class
        /// RESOLVES: <c>System.Collections.Generic.List&lt;System.Int32&gt;.Enumerator</c> reaches
        /// <c>GetTypeByMetadataName</c> as <c>System.Collections.Generic.List`1+Enumerator</c>.
        /// Without it every generic-typed receiver — nested or not — was NotFound, which the
        /// §8.5 receiver derivation reports as INCOMPLETE, which nulls §10.2's cache key: an
        /// unconditional ~25 s AOT publish on EVERY build of such a program, forever.</para>
        ///
        /// <para><b>Ordering is the safety property.</b> The literal ladder is emitted FIRST and in
        /// full, so a metadata name that genuinely contains angle brackets — every
        /// compiler-generated <c>&lt;&gt;c__DisplayClass</c> — still resolves through its own
        /// spelling and cannot be displaced by the derived one: <see cref="LookupUncached"/>'s
        /// fast path returns on the first candidate that resolves, so an added candidate can never
        /// change a RESOLVED answer.</para>
        ///
        /// <para>It can, however, change a NotFound into either outcome. The MISS path evaluates
        /// every candidate rather than stopping at the first, precisely so absence and ambiguity
        /// stay distinguishable — so a derived candidate DECLARED IN TWO ASSEMBLIES makes the
        /// answer <see cref="NetTypeLookupOutcome.Ambiguous"/> (BL6023, "drop one of the
        /// references") rather than NotFound (BL6016, "no such type"). That is the more accurate
        /// of the two diagnostics for a name that genuinely exists twice, and it is the same
        /// answer the metadata spelling of that type would already have produced.</para>
        ///
        /// <para>Cost runs the right way. The second ladder only exists for a spelling containing
        /// <c>&lt;</c>, and for those it is what makes the FAST path hit at all — every one of them
        /// used to fall through to the miss path, which scans every referenced assembly per
        /// candidate at ~17 ms. A spelling that still misses now pays that scan twice over; it is
        /// memoized like any other answer.</para>
        /// </summary>
        private static IReadOnlyList<string> CandidateMetadataNames(string fullName)
        {
            var candidates = new List<string>();
            AddNestingLadder(candidates, fullName);

            var metadataForm = MetadataFormOfGenericSpelling(fullName);
            if (metadataForm != null)
                AddNestingLadder(candidates, metadataForm);

            return candidates;
        }

        private static void AddNestingLadder(List<string> candidates, string fullName)
        {
            candidates.Add(fullName);
            var chars = fullName.ToCharArray();
            for (var i = chars.Length - 1; i > 0; i--)
            {
                if (chars[i] != '.')
                    continue;
                chars[i] = '+';
                candidates.Add(new string(chars));
            }
        }

        /// <summary>
        /// <paramref name="spelling"/> rewritten from C# generic syntax into a metadata name —
        /// <c>Ns.Outer&lt;A, B&gt;.Inner</c> → <c>Ns.Outer`2+Inner</c> — or <b>null</b> when it
        /// carries no type-argument list (nothing to translate) or is not well-formed enough to
        /// translate confidently.
        ///
        /// <para>Only the argument COUNT survives, because that is all a metadata name can carry:
        /// <c>List&lt;System.Int32&gt;</c> and <c>List&lt;T&gt;</c> both resolve to the
        /// <c>List`1</c> DEFINITION. That is the right answer for every question asked through a
        /// name here — existence, accessibility, value-type-ness, members — all of which are
        /// properties of the definition. It is deliberately NOT enough to distinguish two
        /// constructions, which is exactly why <see cref="TypeName"/> keeps the C# form as the
        /// spelling it hands to the mangler.</para>
        ///
        /// <para>A dot AFTER a type-argument list is nesting, unconditionally — C# has no namespace
        /// inside a constructed type — so those become <c>+</c> here rather than being left to the
        /// progressive ladder to guess. Leading dots stay dots and keep their ladder, since a
        /// namespace and an outer type are still indistinguishable there.</para>
        ///
        /// <para>Array and pointer spellings (<c>List&lt;System.Int32&gt;[]</c>) translate into a
        /// candidate that simply does not resolve, which is what they do today as well —
        /// <c>GetTypeByMetadataName</c> has no array syntax — so they are not special-cased.</para>
        /// </summary>
        private static string MetadataFormOfGenericSpelling(string spelling)
        {
            if (spelling.IndexOf('<') < 0)
                return null;

            var metadata = new StringBuilder(spelling.Length);
            var sawTypeArguments = false;

            for (var i = 0; i < spelling.Length; i++)
            {
                var c = spelling[i];
                if (c == '.')
                {
                    metadata.Append(sawTypeArguments ? '+' : '.');
                    continue;
                }
                if (c == '>')
                    return null;            // unbalanced — do not guess
                if (c != '<')
                {
                    metadata.Append(c);
                    continue;
                }

                var end = TypeArgumentListEnd(spelling, i, out var arity);
                if (end < 0)
                    return null;            // unterminated — do not guess

                metadata.Append('`').Append(arity.ToString(CultureInfo.InvariantCulture));
                sawTypeArguments = true;
                i = end;
            }

            return metadata.ToString();
        }

        /// <summary>
        /// The index of the <c>&gt;</c> closing the list that opens at <paramref name="start"/>,
        /// and how many arguments it holds; -1 when it never closes. Depth-aware, because an
        /// argument can be generic itself and carry its own commas
        /// (<c>List&lt;Dictionary&lt;A, B&gt;&gt;</c> is arity ONE).
        /// </summary>
        private static int TypeArgumentListEnd(string spelling, int start, out int arity)
        {
            arity = 0;
            var depth = 0;
            var sawArgumentText = false;

            for (var i = start; i < spelling.Length; i++)
            {
                var c = spelling[i];
                if (c == '<')
                {
                    depth++;
                    continue;
                }
                if (c == '>')
                {
                    depth--;
                    if (depth != 0)
                        continue;
                    // An EMPTY list is arity 0 rather than 1 — `<>` is not a type argument, it is
                    // the leading pair of a compiler-generated name that reached here by accident.
                    arity = sawArgumentText ? arity + 1 : 0;
                    return i;
                }
                if (depth == 1 && c == ',')
                    arity++;
                else if (depth >= 1 && !char.IsWhiteSpace(c))
                    sawArgumentText = true;
            }

            return -1;
        }

        // ------------------------------------------------------------------
        // P2a-2 Task 9 — THE SYMBOL-CARRYING SEAM (spec §8.5; the plan's architectural-input
        // blockquote).
        // ------------------------------------------------------------------

        /// <summary>
        /// The CONSTRUCTED symbol for a C# type spelling — <c>List&lt;System.Int32&gt;</c> as the
        /// <c>List&lt;Int32&gt;</c> instantiation, not the <c>List&lt;T&gt;</c> definition
        /// <see cref="ResolveTypeDetailed"/> answers with.
        ///
        /// <para><b>Why this seam had to exist.</b> Everything else in this class is reached BY
        /// NAME, and a name resolves to the DEFINITION — <see cref="Lookup"/> goes through
        /// <c>GetTypeByMetadataName</c>, and a metadata name cannot express a construction
        /// (<c>List`1</c> says nothing about <c>&lt;Int32&gt;</c>). That is the right answer for
        /// every question asked through a name (existence, accessibility, kind, members), which
        /// is why it was never a problem before. §8.5 asks a question it is the WRONG answer
        /// for: "what does <c>For Each x In someNetList</c> bind <c>x</c> to?" The definition
        /// says <c>T</c> — an open type parameter with no wire form — while the construction
        /// says <c>System.Int32</c>. So the argument list is parsed back out of the spelling
        /// <see cref="TypeName"/> produced and re-applied with
        /// <see cref="INamedTypeSymbol.Construct(ITypeSymbol[])"/>.</para>
        ///
        /// <para><b>Round-trip safe by construction:</b> the arguments are resolved through the
        /// same <see cref="Lookup"/> that accepts this class's own spellings (Task 8b), so a
        /// name this class PRODUCES is a name it CONSTRUCTS. Returns null rather than guessing
        /// when the spelling is not well-formed, when an argument does not resolve, or when the
        /// arity does not match the definition — a wrong construction here would type a loop
        /// variable as the wrong element type, which is a silently wrong program.</para>
        ///
        /// <para>⚠ <b>This seam is what makes §7.3's construction collision REACHABLE.</b>
        /// <c>List&lt;int&gt;.Enumerator.MoveNext</c> and
        /// <c>List&lt;string&gt;.Enumerator.MoveNext</c> could not previously be spelled at all,
        /// so <c>NetNameMangler</c>'s collision-freedom over two constructions of one nested
        /// generic was theoretical (see <c>NetNameManglerTests.
        /// CollisionFreedomOverTwoConstructionsOfOneNestedGeneric</c>). It is not any more:
        /// <see cref="TypeName"/> keeps each construction's own argument list, so the two mangle
        /// apart and <c>NetShimGenerator.Plan</c>'s <c>if (!seen.Add(name)) continue;</c> cannot
        /// silently drop the second.</para>
        /// </summary>
        internal INamedTypeSymbol ConstructedTypeSymbol(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return null;

            var definition = Lookup(fullName).Symbol;
            if (definition == null) return null;

            if (definition.Arity == 0)
            {
                // ⛔ Arity 0 does NOT mean "already closed" — a type NESTED in a generic
                // declares none of its own. `List<System.Int32>.Enumerator` resolves through the
                // metadata form `List`1+Enumerator` to a symbol whose Arity is 0, so returning
                // it unchecked would hand back `List<T>.Enumerator` — an OPEN type — from a
                // method whose contract is "construct, or answer null; never guess" (P2a-2
                // Task-9 review item 2). `EnumerableElementTypeName` degrades to null on that,
                // but `ConstructedIndexer` would describe an open-T indexer: a wrong descriptor,
                // a wrong export, and CS0246/CS0012 inside generated C# AFTER the ~25 s AOT
                // publish — precisely the late-failure class this seam exists to remove.
                //
                // ⚠ A ROUND-TRIP TEST IS NOT ENOUGH, and measuring said so: TypeName spells
                // `List<T>.Enumerator` back byte-identically, because the spelling is FAITHFUL —
                // it is just open. Openness is the actual property, so it is what gets tested.
                // The round trip is kept alongside it because it catches the other way in: a
                // caller passing a spelling Lookup accepts but TypeName would not produce
                // (a metadata `Outer+Inner`, a `List`1`), where "the symbol I got back is the
                // one this name means" is no longer guaranteed.
                if (ContainsOpenTypeParameter(definition)) return null;
                return string.Equals(TypeName(definition), fullName, StringComparison.Ordinal)
                    ? definition
                    : null;
            }

            var open = fullName.IndexOf('<');
            if (open < 0) return null;                       // arity > 0 but no argument list
            var end = TypeArgumentListEnd(fullName, open, out var arity);
            if (end < 0 || arity != definition.Arity) return null;

            var arguments = new List<ITypeSymbol>(arity);
            foreach (var argument in SplitTypeArguments(fullName, open + 1, end))
            {
                var resolved = ResolveArgumentSymbol(argument);
                if (resolved == null) return null;           // never guess
                arguments.Add(resolved);
            }

            try
            {
                return definition.OriginalDefinition.Construct(arguments.ToArray());
            }
            catch (ArgumentException)
            {
                // A constraint violation in a spelling we did not author. Refusing is right —
                // the caller degrades to "no element type", which is a positioned BL6019.
                return null;
            }
        }

        /// <summary>
        /// The ELEMENT type of a handle-represented .NET collection — <c>System.Int32</c> for
        /// <c>List&lt;System.Int32&gt;</c> and for <c>System.Int32[]</c> — read from
        /// <c>IEnumerable&lt;T&gt;</c>, or null when the type implements no generic
        /// <c>IEnumerable</c>.
        ///
        /// <para><b>Through <c>IEnumerable&lt;T&gt;</c> deliberately, never through a
        /// <c>GetEnumerator()</c> the type happens to declare</b> — §8.5's mutable-struct
        /// enumerator rule. A type with only a struct enumerator and no <c>IEnumerable</c>
        /// answers null here, which the analyzer reports as BL6019 exactly as §8.5 says it
        /// must.</para>
        /// </summary>
        internal string EnumerableElementTypeName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return null;

            // An array's element is not reachable through Lookup (GetTypeByMetadataName has no
            // array syntax) and does not need to be — T[] implements IEnumerable<T> by
            // definition, so the spelling itself is the answer.
            if (fullName.EndsWith("[]", StringComparison.Ordinal))
            {
                var element = fullName.Substring(0, fullName.Length - 2);
                return element.EndsWith("]", StringComparison.Ordinal) ? null : element;
            }

            var symbol = ConstructedTypeSymbol(fullName);
            if (symbol == null) return null;

            foreach (var candidate in Enumerable.Repeat<INamedTypeSymbol>(symbol, 1).Concat(symbol.AllInterfaces))
            {
                if (candidate.OriginalDefinition.SpecialType
                        == SpecialType.System_Collections_Generic_IEnumerable_T
                    && candidate.TypeArguments.Length == 1)
                {
                    return TypeName(candidate.TypeArguments[0]);
                }
            }
            return null;
        }

        /// <summary>
        /// True when any level of <paramref name="type"/>'s CONTAINING chain still carries an
        /// unsubstituted type parameter. The chain — not just the type's own arguments — for the
        /// same reason <c>NetSurfaceCollector.FirstUnmarshalable</c> walks it:
        /// <c>List&lt;T&gt;.Enumerator</c> declares no arguments of its OWN and inherits its
        /// <c>T</c> from its container, which is every bit as unspellable in a monomorphic C
        /// export.
        /// </summary>
        private static bool ContainsOpenTypeParameter(INamedTypeSymbol type)
        {
            for (var level = type; level != null; level = level.ContainingType)
            {
                foreach (var argument in level.TypeArguments)
                {
                    if (FirstOpenTypeParameter(argument) != null)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// An open type parameter anywhere inside <paramref name="type"/>, or null. Mirrors
        /// <c>NetSurfaceCollector.FirstOpenTypeParameter</c>; the two are deliberately separate
        /// because that one is the §7.2 admissibility filter over Roslyn symbols the collector
        /// already holds, and this one is the seam's own "never guess" guard.
        /// </summary>
        private static ITypeSymbol FirstOpenTypeParameter(ITypeSymbol type)
        {
            switch (type)
            {
                case ITypeParameterSymbol parameter:
                    return parameter;
                case IArrayTypeSymbol array:
                    return FirstOpenTypeParameter(array.ElementType);
                case IPointerTypeSymbol pointer:
                    return FirstOpenTypeParameter(pointer.PointedAtType);
                case INamedTypeSymbol named:
                    foreach (var argument in named.TypeArguments)
                    {
                        var open = FirstOpenTypeParameter(argument);
                        if (open != null)
                            return open;
                    }
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// The public INDEXER declared on a CONSTRUCTED type, described with the construction's
        /// types (<c>List&lt;System.Int32&gt;.this[System.Int32]</c> returns <c>System.Int32</c>,
        /// not the open <c>T</c> the definition would report), or null when the type declares
        /// none.
        ///
        /// <para>Spec §8.5's indexer row: an <c>ArrayAccessExpressionNode</c> on a resolved .NET
        /// type lowers to <c>get_Item</c>/<c>set_Item</c>, "which the collector must collect even
        /// though the source never names it". The descriptor is the resolver's ordinary Property
        /// shape — <see cref="DescribeMember"/> already records an indexer's index parameters,
        /// which is what stops the duplicate collapse from eating one of two indexers — so
        /// <c>NetShimGenerator</c>'s existing "Property with parameters ⇒ <c>target[args]</c>"
        /// arm spells the READ and <see cref="NetAccessorSynthesis.SetterFor"/> builds the WRITE.</para>
        ///
        /// <para>Only SINGLE-index indexers are answered. §8.5 v1 has no rule for a multi-index
        /// indexer, and picking one of several arbitrarily is how a call reaches the wrong
        /// member; the caller degrades to a positioned refusal instead.</para>
        /// </summary>
        internal NetMemberDescriptor ConstructedIndexer(string fullName)
        {
            var symbol = ConstructedTypeSymbol(fullName);
            if (symbol == null) return null;

            for (var type = symbol;
                 type != null && type.SpecialType != SpecialType.System_Object;
                 type = type.BaseType)
            {
                foreach (var member in type.GetMembers())
                {
                    if (member is IPropertySymbol { IsIndexer: true, Parameters.Length: 1 } indexer
                        && indexer.DeclaredAccessibility == Accessibility.Public)
                    {
                        return DescribeMember(indexer);
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// One resolved type argument. Accepts this class's own spellings (a nested construction
        /// recurses through <see cref="ConstructedTypeSymbol"/>) and array spellings.
        /// </summary>
        private ITypeSymbol ResolveArgumentSymbol(string spelling)
        {
            spelling = spelling.Trim();
            if (spelling.Length == 0) return null;

            if (spelling.EndsWith("[]", StringComparison.Ordinal))
            {
                var element = ResolveArgumentSymbol(spelling.Substring(0, spelling.Length - 2));
                return element == null ? null : _compilation.CreateArrayTypeSymbol(element);
            }

            return spelling.IndexOf('<') >= 0
                ? ConstructedTypeSymbol(spelling)
                : Lookup(spelling).Symbol;
        }

        /// <summary>
        /// The top-level, comma-separated arguments between <paramref name="start"/> and the
        /// closing <c>&gt;</c> at <paramref name="end"/>. Depth-aware: a nested construction
        /// carries its own commas (<c>Dictionary&lt;A, List&lt;B, C&gt;&gt;</c>).
        /// </summary>
        private static IEnumerable<string> SplitTypeArguments(string spelling, int start, int end)
        {
            var depth = 0;
            var segment = start;
            for (var i = start; i < end; i++)
            {
                var c = spelling[i];
                if (c == '<') depth++;
                else if (c == '>') depth--;
                else if (c == ',' && depth == 0)
                {
                    yield return spelling.Substring(segment, i - segment);
                    segment = i + 1;
                }
            }
            yield return spelling.Substring(segment, end - segment);
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
        ///
        /// <para><b>The spelling is C# TYPE SYNTAX, not a metadata name</b> — <c>Outer&lt;A&gt;.Inner</c>,
        /// never <c>Outer`1+Inner</c>. Forced from two directions at once and the two agree:
        /// <c>NetShimGenerator.Qualified</c> emits this string after a bare <c>global::</c> prefix,
        /// so it has to be something <c>csc</c> accepts; and a metadata name cannot express a
        /// CONSTRUCTED generic at all (<c>List`1</c> says nothing about <c>&lt;System.Int32&gt;</c>),
        /// so it would erase a distinction §7.3 needs — <c>List&lt;int&gt;.Enumerator.MoveNext()</c>
        /// and <c>List&lt;string&gt;.Enumerator.MoveNext()</c> are two members that must not share
        /// one export slot. <see cref="ReceiverSyntax"/> already builds exactly this shape for the
        /// overload probe; the lookup side meets it in <see cref="CandidateMetadataNames"/>, which
        /// derives the metadata form back out of it, so <b>a name this method PRODUCES is a name
        /// <see cref="Lookup"/> RESOLVES</b>. That round trip is the property P2a-2 Task 8b existed
        /// to restore, and it is asserted in both directions
        /// (<c>NetTypeResolverTests.NestedGenericSpellingRoundTripsBackThroughLookup</c>).</para>
        /// </summary>
        /// <remarks>
        /// INTERNAL rather than private purely as a test seam: §7.3's collision-freedom claim is
        /// about the STRING this produces for two distinct constructed nesting levels
        /// (<c>List&lt;int&gt;.Enumerator</c> vs <c>List&lt;string&gt;.Enumerator</c>), and neither
        /// is reachable through a by-NAME entry point — a metadata name cannot spell either one.
        /// Same seam-for-testability precedent as <c>CppProjectBuilder.ValueTypeReceiverNames</c>.
        /// </remarks>
        internal static string TypeName(ITypeSymbol type)
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

                return QualifiedName(named);
            }

            // Type parameters, dynamic, function pointers: nothing to normalize.
            return type.ToDisplayString(FullNameFormat);
        }

        /// <summary>
        /// Namespace + containing types + name, with EVERY level carrying its own type-argument
        /// list: <c>System.Collections.Generic.List&lt;System.Int32&gt;.Enumerator</c>.
        ///
        /// <para><b>Walking the containing chain by <c>type.Name</c> dropped the containing
        /// generic's arity AND its arguments</b>, which spelled <c>List&lt;T&gt;.Enumerator</c> as
        /// <c>System.Collections.Generic.List.Enumerator</c> — a name this class's own
        /// <see cref="Lookup"/> answered <see cref="NetTypeLookupOutcome.NotFound"/> for, and which
        /// <c>NetShimGenerator</c> would have emitted as the uncompilable
        /// <c>global::System.Collections.Generic.List.Enumerator</c>. Two things then went wrong
        /// silently rather than loudly (P2a-2 Task 8b): the §8.5 value-type receiver set, which is a
        /// BY-NAME lookup, answered "reference type" for every enumerator struct — so the shim
        /// reached it through an unboxing cast and <c>MoveNext</c> mutated the temporary FOREVER,
        /// with no diagnostic — and two constructions of one nested type mangled to a single §7.3
        /// export name, which §12.4 makes a wrong-member call rather than a build error.</para>
        ///
        /// <para>Arity is carried by the ARGUMENTS, not by a <c>`n</c> suffix, because this is a C#
        /// spelling (see <see cref="TypeName"/>); <see cref="CandidateMetadataNames"/> converts one
        /// into the other. Roslyn's <see cref="INamedTypeSymbol.TypeArguments"/> is per-symbol — a
        /// nested type does NOT repeat its container's arguments — which is the same per-segment
        /// shape metadata names use, so the two forms translate level for level.</para>
        /// </summary>
        private static string QualifiedName(INamedTypeSymbol named)
        {
            var parts = new List<string>();
            for (var type = named; type != null; type = type.ContainingType)
                parts.Insert(0, Segment(type));

            var containingNamespace = named.ContainingNamespace;
            if (containingNamespace != null && !containingNamespace.IsGlobalNamespace)
                parts.Insert(0, containingNamespace.ToDisplayString());

            return string.Join(".", parts);
        }

        /// <summary>One nesting level: its name plus its OWN type arguments, if any.</summary>
        private static string Segment(INamedTypeSymbol type) =>
            type.TypeArguments.Length == 0
                ? type.Name
                : type.Name + "<" + string.Join(", ", type.TypeArguments.Select(TypeName)) + ">";

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
                        Describe(property.Parameters),
                        isSettable: IsSettable(property));

                case IFieldSymbol field:
                    return new NetMemberDescriptor(
                        field.MetadataName,
                        TypeName(field.ContainingType),
                        NetMemberCategory.Field,
                        field.IsStatic,
                        arity: 0,
                        TypeName(field.Type),
                        Array.Empty<NetParameterDescriptor>(),
                        // readonly and const fields are writable in metadata terms but not from a
                        // generated shim: `target.X = v` on either is a hard csc error.
                        isSettable: !field.IsReadOnly && !field.IsConst);

                default:
                    return null;   // events, nested types, everything else
            }
        }

        /// <summary>
        /// P2a-2 Task 7b: whether a generated shim could legally spell <c>target.X = value</c> for
        /// this property. Three ways it cannot, all of them ordinary in the framework:
        /// no setter at all (<c>Regex.Options</c>, <c>String.Length</c>), a setter that is not
        /// public (a <c>private set</c> auto-property), and an <c>init</c>-only setter, which C#
        /// permits only inside an object initializer (CS8852) — a shim body is neither.
        /// </summary>
        private static bool IsSettable(IPropertySymbol property) =>
            property.SetMethod is { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false };

        private static IReadOnlyList<NetParameterDescriptor> Describe(
            IEnumerable<IParameterSymbol> parameters) =>
            parameters
                .Select(p => new NetParameterDescriptor(
                    RefKindOf(p.RefKind), TypeName(p.Type), DelegateInvokeSignatureOf(p.Type)))
                .ToList();

        /// <summary>
        /// P2a-2 Task 11 / decision D-P9: a delegate parameter's <c>Invoke</c> signature rendered
        /// as <c>Return(Param,Param)</c>, or null when the type is not a delegate.
        ///
        /// <para>This is the ONLY place the invoke signature is available — it comes off Roslyn's
        /// <see cref="INamedTypeSymbol.DelegateInvokeMethod"/>, and neither
        /// <c>NetShimGenerator</c> (no Roslyn reference) nor <c>NetProxyEmitter</c> (name only)
        /// can reach it. Rendered through <see cref="TypeName"/> so the spelling matches
        /// <see cref="NetParameterDescriptor.TypeFullName"/>'s exactly, rather than
        /// <c>ToDisplayString</c>'s C# shorthand.</para>
        /// </summary>
        private static string DelegateInvokeSignatureOf(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol named) return null;

            var invoke = named.DelegateInvokeMethod;
            if (invoke == null) return null;

            return TypeName(invoke.ReturnType)
                + "("
                + string.Join(",", invoke.Parameters.Select(p => TypeName(p.Type)))
                + ")";
        }

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
