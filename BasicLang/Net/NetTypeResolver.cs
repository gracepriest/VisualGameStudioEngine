using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BasicLang.Net
{
    /// <summary>What a .NET type is, coarsely. Spec §6.1 makes kind part of the answer.</summary>
    internal enum NetTypeKind { Class, Struct, Interface, Enum, Delegate, Other }

    /// <summary>
    /// The four member categories spec §7.2 admits into a generated surface: "public
    /// constructors, methods, properties and fields".
    /// </summary>
    internal enum NetMemberKind { Constructor, Method, Property, Field }

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
    /// value equality means what it appears to mean (contrast <see cref="NetMemberInfo"/>).
    /// </summary>
    /// <param name="FullName">
    /// Namespace-qualified, nested types spelled with '.', generic type parameters spelled
    /// <c>&lt;T&gt;</c>. See <see cref="NetTypeResolver.ResolveType"/> for the round-trip caveat on
    /// generics.
    /// </param>
    internal sealed record NetTypeInfo(string FullName, NetTypeKind Kind, bool IsPublic);

    /// <summary>
    /// The outcome of a type lookup. <see cref="Type"/> is non-null only for
    /// <see cref="NetTypeLookupOutcome.Resolved"/> — an ambiguous reference has no single winner,
    /// and picking one silently is how a build ends up bound to whichever assembly happened to be
    /// enumerated first.
    /// </summary>
    internal sealed record NetTypeLookupResult(NetTypeLookupOutcome Outcome, NetTypeInfo Type);

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
    internal sealed class NetMemberInfo
    {
        public NetMemberInfo(
            string name,
            string declaringTypeFullName,
            NetMemberKind kind,
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

        public NetMemberKind Kind { get; }

        public bool IsStatic { get; }

        /// <summary>
        /// Return type for a method, value type for a property or field. For a constructor this
        /// is <c>System.Void</c>, which is what metadata says.
        /// </summary>
        public string TypeFullName { get; }

        /// <summary>
        /// Parameter types in declaration order, fully qualified and never C# keywords — empty for
        /// properties and fields. <c>bool</c> and <c>System.Boolean</c> would mangle to different
        /// identifiers for one method, so the format is fixed at the fully-qualified spelling.
        /// </summary>
        public IReadOnlyList<string> ParameterTypeFullNames { get; }

        public override string ToString() =>
            $"{(IsStatic ? "static " : "")}{Kind} {TypeFullName} {DeclaringTypeFullName}.{Name}"
            + (Kind == NetMemberKind.Property || Kind == NetMemberKind.Field
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
        /// </summary>
        private readonly Dictionary<string, CachedLookup> _cache =
            new Dictionary<string, CachedLookup>(StringComparer.Ordinal);

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
        /// arity. (Consequence: <see cref="NetTypeInfo.FullName"/> does not round-trip through this
        /// method for a generic type — it reports <c>List&lt;T&gt;</c>, the readable form, because
        /// its consumers are diagnostics and the mangler, not this lookup.)</para>
        /// </summary>
        public NetTypeInfo ResolveType(string fullName) => ResolveTypeDetailed(fullName).Type;

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
        /// <para><b>Deliberate exclusions, all with the same reason — they are not callable
        /// members of the surface:</b> property/event accessors (<c>get_X</c>/<c>set_X</c>: a
        /// property is ONE member, or every property costs three exports and three proxy slots),
        /// events, nested types (resolve them by name instead), static constructors, finalizers,
        /// user-defined operators and conversions, and anything implicitly declared (an enum's
        /// <c>value__</c>, a struct's synthesized parameterless constructor). Non-public members
        /// are excluded because the shim cannot call them.</para>
        /// </summary>
        public IReadOnlyList<NetMemberInfo> GetMembers(string fullName)
        {
            var symbol = Lookup(fullName).Symbol;
            if (symbol == null)
                return Array.Empty<NetMemberInfo>();

            var members = new List<NetMemberInfo>();
            for (var type = symbol;
                 type != null && type.SpecialType != SpecialType.System_Object;
                 type = type.BaseType)
            {
                foreach (var member in type.GetMembers())
                {
                    var described = DescribeMember(member);
                    if (described != null)
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

        private static NetTypeInfo Describe(INamedTypeSymbol symbol) =>
            symbol == null
                ? null
                : new NetTypeInfo(
                    symbol.ToDisplayString(FullNameFormat),
                    KindOf(symbol),
                    symbol.DeclaredAccessibility == Accessibility.Public);

        private static NetTypeKind KindOf(INamedTypeSymbol symbol)
        {
            switch (symbol.TypeKind)
            {
                case TypeKind.Class: return NetTypeKind.Class;
                case TypeKind.Struct: return NetTypeKind.Struct;
                case TypeKind.Interface: return NetTypeKind.Interface;
                case TypeKind.Enum: return NetTypeKind.Enum;
                case TypeKind.Delegate: return NetTypeKind.Delegate;
                default: return NetTypeKind.Other;
            }
        }

        /// <summary>
        /// The <see cref="NetMemberInfo"/> for <paramref name="member"/>, or null if it is not part
        /// of the surface. See <see cref="GetMembers"/> for the exclusion list and its rationale.
        /// </summary>
        private static NetMemberInfo DescribeMember(ISymbol member)
        {
            if (member.DeclaredAccessibility != Accessibility.Public || member.IsImplicitlyDeclared)
                return null;

            switch (member)
            {
                case IMethodSymbol method:
                    var kind = method.MethodKind == MethodKind.Constructor
                        ? NetMemberKind.Constructor
                        : method.MethodKind == MethodKind.Ordinary
                            ? NetMemberKind.Method
                            : (NetMemberKind?)null;
                    if (kind == null)
                        return null;
                    return new NetMemberInfo(
                        method.MetadataName,
                        method.ContainingType.ToDisplayString(FullNameFormat),
                        kind.Value,
                        method.IsStatic,
                        method.ReturnType.ToDisplayString(FullNameFormat),
                        method.Parameters.Select(p => p.Type.ToDisplayString(FullNameFormat)).ToList());

                case IPropertySymbol property:
                    return new NetMemberInfo(
                        property.MetadataName,
                        property.ContainingType.ToDisplayString(FullNameFormat),
                        NetMemberKind.Property,
                        property.IsStatic,
                        property.Type.ToDisplayString(FullNameFormat),
                        Array.Empty<string>());

                case IFieldSymbol field:
                    return new NetMemberInfo(
                        field.MetadataName,
                        field.ContainingType.ToDisplayString(FullNameFormat),
                        NetMemberKind.Field,
                        field.IsStatic,
                        field.Type.ToDisplayString(FullNameFormat),
                        Array.Empty<string>());

                default:
                    return null;   // events, nested types, everything else
            }
        }
    }
}
