using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
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
