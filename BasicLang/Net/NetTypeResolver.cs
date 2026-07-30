using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BasicLang.Net
{
    // ----------------------------------------------------------------------
    // NAMING: these are the "…Descriptor"/"…Category" types ON PURPOSE.
    //
    // BasicLang.Compiler.SemanticAnalysis ALREADY declares public NetTypeInfo, NetMemberInfo,
    // NetParameterInfo and NetMemberKind (TypeRegistry.cs:670-755), consumed by
    // SemanticAnalyzer.cs, SymbolTable.cs, ExternalLibraryLoader.cs and four LSP files. Those are
    // the LSP's loose IntelliSense shapes; these are the resolver's precise ones. They are
    // deliberately DISTINCT types and must not be "unified":
    //
    //   * the enums disagree on ordering — the old NetMemberKind is Method=0 … Constructor=5,
    //     this one is Constructor=0 … Field=3 — so any int round-trip between them silently
    //     mismaps a member's kind;
    //   * Task 7 modifies TypeRegistry.cs and Task 8 modifies SemanticAnalyzer.cs, both files
    //     where the OLD names are already in scope, and any file importing both namespaces would
    //     get CS0104 on the unqualified name.
    //
    // Renaming them here rather than there is the cheap direction: this namespace has one
    // consumer, that one has seven files.
    // ----------------------------------------------------------------------

    /// <summary>What a .NET type is, coarsely. Spec §6.1 makes kind part of the answer.</summary>
    internal enum NetTypeCategory { Class, Struct, Interface, Enum, Delegate, Other }

    /// <summary>
    /// The four member categories spec §7.2 admits into a generated surface: "public
    /// constructors, methods, properties and fields".
    /// </summary>
    internal enum NetMemberCategory { Constructor, Method, Property, Field }

    /// <summary>
    /// Why a type lookup answered the way it did.
    ///
    /// <para><b>The three cases must stay distinct.</b>
    /// <c>Compilation.GetTypeByMetadataName</c> returns null for BOTH absence and ambiguity, and
    /// spec §11.4 gives those two different diagnostics — BL6016 ".NET type not found" versus
    /// BL6023 "ambiguous .NET type reference". Collapsing them here makes one of the two a lie in
    /// Task 8, and "type not found" for a type the user can see declared twice is the least
    /// actionable message the compiler could produce.</para>
    /// </summary>
    internal enum NetTypeLookupOutcome { Resolved, NotFound, Ambiguous }

    /// <summary>
    /// A resolved .NET type. A record is safe here — every member is a string, enum or bool, so
    /// value equality means what it appears to mean (contrast <see cref="NetMemberDescriptor"/>).
    /// </summary>
    /// <param name="FullName">
    /// Namespace-qualified, nested types spelled with '.', generic type parameters spelled
    /// <c>&lt;T&gt;</c>. See <see cref="NetTypeResolver.ResolveType"/> for the round-trip caveat on
    /// generics.
    /// </param>
    internal sealed record NetTypeDescriptor(string FullName, NetTypeCategory Kind, bool IsPublic);

    /// <summary>
    /// The outcome of a type lookup. <see cref="Type"/> is non-null only for
    /// <see cref="NetTypeLookupOutcome.Resolved"/> — an ambiguous reference has no single winner,
    /// and picking one silently is how a build ends up bound to whichever assembly happened to be
    /// enumerated first.
    /// </summary>
    internal sealed record NetTypeLookupResult(NetTypeLookupOutcome Outcome, NetTypeDescriptor Type);

    /// <summary>
    /// One member of a .NET type, carrying exactly the three things
    /// <c>NetNameMangler</c> mangles from (spec §7.3): the fully-qualified declaring type, the
    /// member's metadata name, and its parameter types.
    ///
    /// <para><b>Deliberately a class and not a record</b>, for the reason
    /// <see cref="NetReferenceClosure"/> documents: record equality over an
    /// <see cref="IReadOnlyList{T}"/> member degenerates to REFERENCE equality, so
    /// <c>==</c> on two members with identical parameter lists would look like a content
    /// comparison and behave like an identity one. Reference equality is the honest default here,
    /// and <see cref="ToString"/> covers the only thing record synthesis was actually buying us —
    /// readable assertion failures.</para>
    /// </summary>
    internal sealed class NetMemberDescriptor
    {
        public NetMemberDescriptor(
            string name,
            string declaringTypeFullName,
            NetMemberCategory kind,
            bool isStatic,
            string typeFullName,
            IReadOnlyList<string> parameterTypeFullNames)
        {
            Name = name;
            DeclaringTypeFullName = declaringTypeFullName;
            Kind = kind;
            IsStatic = isStatic;
            TypeFullName = typeFullName;
            ParameterTypeFullNames = parameterTypeFullNames;
        }

        /// <summary>
        /// The member's METADATA name: <c>".ctor"</c> for a constructor, <c>"Item"</c> for an
        /// indexer, the plain name otherwise (CLR method metadata names carry no generic-arity
        /// suffix). <see cref="Kind"/> is what distinguishes a constructor, not the name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The type that DECLARES this member, fully qualified — for an inherited member that is
        /// the base type, not the type that was queried. §7.3 requires the mangler to be
        /// collision-free over the fully-qualified declaring type, so reporting the queried type
        /// here would collapse <c>Base.Foo</c> and <c>Derived.Foo</c> into one export.
        /// </summary>
        public string DeclaringTypeFullName { get; }

        public NetMemberCategory Kind { get; }

        public bool IsStatic { get; }

        /// <summary>
        /// Return type for a method, value type for a property or field. For a constructor this
        /// is <c>System.Void</c>, which is what metadata says.
        /// </summary>
        public string TypeFullName { get; }

        /// <summary>
        /// Parameter types in declaration order, fully qualified and never C# keywords. Empty for
        /// fields and for ordinary properties; an INDEXER's parameters ARE recorded here, because
        /// an indexer is a parameterized member and two of them differ only by those types.
        /// <c>bool</c> and <c>System.Boolean</c> would mangle to different identifiers for one
        /// method, so the format is fixed at the fully-qualified spelling.
        ///
        /// <para>Recording indexer parameters is load-bearing, not tidiness: it is what stops
        /// <see cref="NetTypeResolver.GetMembers"/>'s duplicate-collapsing from eating one of
        /// them. Measured on <c>System.Collections.Specialized.NameValueCollection</c>, whose
        /// <c>this[int]</c> and <c>this[string]</c> present one identity without this and two
        /// with it.</para>
        /// </summary>
        public IReadOnlyList<string> ParameterTypeFullNames { get; }

        public override string ToString() =>
            $"{(IsStatic ? "static " : "")}{Kind} {TypeFullName} {DeclaringTypeFullName}.{Name}"
            + (ParameterTypeFullNames.Count == 0
               && (Kind == NetMemberCategory.Property || Kind == NetMemberCategory.Field)
                ? ""
                : "(" + string.Join(", ", ParameterTypeFullNames) + ")");
    }

    /// <summary>
    /// The compiler's .NET type knowledge (spec §6.1), backed by a Roslyn
    /// <see cref="CSharpCompilation"/> over the reference closure
    /// <see cref="NetReferenceResolver"/> produces.
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
    /// <para><b>Nothing consumes this yet.</b> P2a-1 changes the behavior of not one existing
    /// program; Task 8 wires the resolver into the analyzer warning-only.</para>
    /// </summary>
    internal sealed class NetTypeResolver
    {
        /// <summary>
        /// Fully-qualified spelling with no <c>global::</c> prefix and no C# keyword aliases:
        /// <c>System.Int32</c>, never <c>int</c>. Two spellings of one type would mangle to two
        /// export names (§7.3), and <c>UseSpecialTypes</c> — which Roslyn's default display format
        /// enables — is exactly how that happens.
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

        private readonly List<NetReferenceDiagnostic> _diagnostics;

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
        /// failure shape available for a language server. Roslyn's own symbol APIs are already
        /// thread-safe, so this dictionary was the only unsafe thing here.</para>
        ///
        /// <para><see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}.GetOrAdd(TKey, Func{TKey, TValue})"/>
        /// may run the factory more than once under contention. That is harmless: the lookup is a
        /// pure function of the compilation, so duplicate work yields an equal result.</para>
        /// </summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CachedLookup> _cache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, CachedLookup>(StringComparer.Ordinal);

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
            List<NetReferenceDiagnostic> diagnostics)
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

            return new NetTypeResolver(compilation, assemblies, diagnostics);
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
        /// assemblies precisely so that Roslyn never sees <c>coreclr.dll</c>.
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
        /// its base types (spec §7.2), or empty when the type does not exist.
        ///
        /// <para><b>Empty, not an exception</b>, for an unknown type: this is called from the
        /// analyzer and the IntelliSense path, where a user typo must not become a crashed build
        /// or a dead LSP request.</para>
        ///
        /// <para><b>The base walk stops at (and excludes) <see cref="object"/></b>, per §7.2's
        /// "excluding <c>System.Object</c>'s members unless overridden": an override is a member of
        /// the overriding type and so is included anyway, while <c>ReferenceEquals</c> and friends
        /// are noise that would cost a shim export apiece. Intermediate bases —
        /// <c>System.ValueType</c>, <c>System.Enum</c>, <c>System.MarshalByRefObject</c> — are
        /// included, because "base types" is what the spec says.</para>
        ///
        /// <para><b>Interfaces are not walked.</b> §7.2 says "the type and its base types". An
        /// interface member reachable on a class is a member of that class.</para>
        ///
        /// <para><b>An override appears ONCE, under its most-derived declaration.</b> A base walk
        /// with no dedup reports both the override and the member it overrides — measured on the
        /// real framework, <c>System.IO.FileStream</c> yields 95 members of which 26
        /// (name, parameters, static-ness) identities appear TWICE (<c>Read(Byte[], Int32, Int32)</c>
        /// declared by both <c>FileStream</c> and <c>Stream</c>), <c>MemoryStream</c> 21, and any
        /// enum 3 (<c>Equals</c>/<c>GetHashCode</c>/<c>ToString</c> from both <c>Enum</c> and
        /// <c>ValueType</c>). That is not cosmetic: Task 5 filters this list by name and matches
        /// parameter types, so two identical candidates become <c>Ambiguous</c> and a plain
        /// <c>fs.Read(buf, 0, n)</c> earns a spurious BL6018; Task 12 would emit two proxy slots
        /// per override. The most-derived declaration wins because that is the one that actually
        /// runs, and the walk visits derived before base so first-seen is most-derived.</para>
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
        public IReadOnlyList<NetMemberDescriptor> GetMembers(string fullName)
        {
            var symbol = Lookup(fullName).Symbol;
            if (symbol == null)
                return Array.Empty<NetMemberDescriptor>();

            var members = new List<NetMemberDescriptor>();
            var seen = new HashSet<(NetMemberCategory, string, bool, string)>();
            for (var type = symbol;
                 type != null && type.SpecialType != SpecialType.System_Object;
                 type = type.BaseType)
            {
                foreach (var member in type.GetMembers())
                {
                    var described = DescribeMember(member);
                    if (described == null)
                        continue;

                    // Presentation identity, NOT declaring type: an override and the member it
                    // overrides differ only in the latter, which is precisely why they collide.
                    // Kind participates so a base property and a derived method of the same name
                    // do not shadow each other; parameter types participate so two indexers
                    // survive (see NetMemberDescriptor.ParameterTypeFullNames).
                    if (!seen.Add((described.Kind, described.Name, described.IsStatic,
                                   string.Join(",", described.ParameterTypeFullNames))))
                        continue;

                    members.Add(described);
                }
            }
            return members;
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
        // Description
        // ------------------------------------------------------------------

        private static NetTypeDescriptor Describe(INamedTypeSymbol symbol) =>
            symbol == null
                ? null
                : new NetTypeDescriptor(
                    symbol.ToDisplayString(FullNameFormat),
                    KindOf(symbol),
                    symbol.DeclaredAccessibility == Accessibility.Public);

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
                        method.ContainingType.ToDisplayString(FullNameFormat),
                        kind.Value,
                        method.IsStatic,
                        method.ReturnType.ToDisplayString(FullNameFormat),
                        method.Parameters.Select(p => p.Type.ToDisplayString(FullNameFormat)).ToList());

                case IPropertySymbol property:
                    return new NetMemberDescriptor(
                        property.MetadataName,
                        property.ContainingType.ToDisplayString(FullNameFormat),
                        NetMemberCategory.Property,
                        property.IsStatic,
                        property.Type.ToDisplayString(FullNameFormat),
                        // An indexer's parameters, empty for an ordinary property. Without these
                        // two indexers present one identity and GetMembers' dedup eats one.
                        property.Parameters.Select(p => p.Type.ToDisplayString(FullNameFormat)).ToList());

                case IFieldSymbol field:
                    return new NetMemberDescriptor(
                        field.MetadataName,
                        field.ContainingType.ToDisplayString(FullNameFormat),
                        NetMemberCategory.Field,
                        field.IsStatic,
                        field.Type.ToDisplayString(FullNameFormat),
                        Array.Empty<string>());

                default:
                    return null;   // events, nested types, everything else
            }
        }
    }
}
