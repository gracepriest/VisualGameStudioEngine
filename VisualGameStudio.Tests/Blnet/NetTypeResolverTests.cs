using BasicLang.Net;
using BasicLang.Compiler.ProjectSystem;   // NOT BasicLang.ProjectSystem — see ProjectFile.cs:8
using BasicLang.Compiler.SemanticAnalysis;   // TypeRegistry, NetTypeInfo, NetMemberKind
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// The framework assembly set the resolver tests build on.
///
/// <para><b>This deliberately does NOT re-derive the set from
/// <c>TRUSTED_PLATFORM_ASSEMBLIES</c>.</b> Raw TPA is the HOST PROCESS's assembly set, not the
/// framework's: in this test host it also contains Avalonia, AvaloniaEdit, NUnit and ~40 other
/// non-framework managed DLLs, so a resolver built from it would have ambient visibility into the
/// test host's dependency graph and would not be representative of the CLI. Worse, enumerating
/// the framework directory instead picks up the NATIVE DLLs living there (<c>coreclr.dll</c>,
/// <c>clrjit.dll</c>, <c>mscordbi.dll</c>, <c>hostpolicy.dll</c>).
/// <see cref="NetReferenceResolver"/> already intersects the two — TPA contributes "managed and
/// trusted", the shared-framework directory contributes "framework, not the host's own
/// dependencies" — and <c>NetReferenceResolverTests</c> pins that intersection. Asking the product
/// for the set keeps ONE derivation in the repo; a parallel one here is exactly the drift the
/// shared-resolver convention exists to prevent.</para>
/// </summary>
internal static class NetTypeResolverTestRefs
{
    /// <summary>
    /// The product's framework set, obtained through the product's own API. A project that
    /// declares nothing still gets <see cref="NetReferenceClosure.FrameworkPaths"/> populated
    /// (spec §6.5), which is precisely the "framework only" closure these tests want. The
    /// <c>.blproj</c> path is never opened — only its directory is used, for <c>HintPath</c>
    /// resolution that no reference here triggers.
    /// </summary>
    internal static IReadOnlyList<string> FrameworkPaths { get; } =
        NetReferenceResolver.Resolve(
            new ProjectFile { Backend = "cpp" },
            Path.Combine(Path.GetTempPath(), "nettyperesolver-framework-only.blproj")).FrameworkPaths;
}

/// <summary>
/// NetTypeResolver is the compiler's first real .NET type knowledge (spec §6.1). Before P2a-1
/// the analyzer accepted any PascalCase identifier as a .NET type, so New Regex(1,2,3)
/// type-checked clean and failed later in csc. Roslyn reads metadata WITHOUT loading assemblies
/// into the process, which is also the fix for TypeRegistry's Assembly.LoadFrom (spec §6.2).
/// </summary>
[TestFixture]
public class NetTypeResolverTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nettyperes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch { Thread.Sleep(200); }
        }
    }

    private static NetTypeResolver FrameworkOnly() => NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths);

    /// <summary>
    /// Compiles <paramref name="source"/> into a real on-disk assembly. Ambiguity, non-public
    /// types and inheritance are all pinned against SYNTHESIZED assemblies rather than against
    /// whatever the shared framework happens to contain, so the assertions are machine- and
    /// runtime-version-independent.
    /// </summary>
    private string EmitProbeAssembly(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            NetTypeResolverTestRefs.FrameworkPaths.Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(_dir, assemblyName + ".dll");
        var result = compilation.Emit(path);
        Assert.That(result.Success, Is.True,
            "the FIXTURE failed to build its probe assembly (not the resolver): " + string.Join("\n",
                result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    // ------------------------------------------------------------------
    // Type existence.
    // ------------------------------------------------------------------

    [Test]
    public void ResolvesAFrameworkType()
    {
        var t = FrameworkOnly().ResolveType("System.Text.RegularExpressions.Regex");

        Assert.That(t, Is.Not.Null);
        Assert.That(t!.FullName, Is.EqualTo("System.Text.RegularExpressions.Regex"));
        Assert.That(t.Kind, Is.EqualTo(NetTypeCategory.Class));
        Assert.That(t.IsPublic, Is.True);
    }

    [Test]
    public void ReturnsNullForATypeThatDoesNotExist()
    {
        Assert.That(FrameworkOnly().ResolveType("System.Text.Rejex"), Is.Null,
            "A miss must be null so the caller can raise BL6016 — never a fabricated TypeInfo, " +
            "which is exactly what the permissive analyzer did before P2a-1.");
    }

    [Test]
    public void ResolveTypeDetailed_ReportsNotFoundDistinctlyFromAmbiguous()
    {
        var result = FrameworkOnly().ResolveTypeDetailed("System.Text.Rejex");

        Assert.That(result.Outcome, Is.EqualTo(NetTypeLookupOutcome.NotFound),
            "Absence and ambiguity both make Compilation.GetTypeByMetadataName return null. " +
            "Task 8 maps absence to BL6016 and ambiguity to BL6023 (spec §11.4), so collapsing " +
            "them here makes one of those two diagnostics a lie. Fix NetTypeResolver.");
        Assert.That(result.Type, Is.Null);
    }

    [Test]
    public void ResolveTypeDetailed_TwoReferencesDeclaringTheSameTypeIsAmbiguous_NotNotFound()
    {
        // A GENUINE ambiguity: two independent assemblies each DECLARING Contoso.Dup. This is not
        // reproducible from the framework set alone (facade assemblies like netstandard.dll only
        // FORWARD types, and forwarders resolve to a single symbol), so the fixture builds it.
        const string source = "namespace Contoso { public class Dup { public int X; } }";
        var a = EmitProbeAssembly("BlnetDupA", source);
        var b = EmitProbeAssembly("BlnetDupB", source);

        var resolver = NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { a, b }));
        var result = resolver.ResolveTypeDetailed("Contoso.Dup");

        Assert.That(result.Outcome, Is.EqualTo(NetTypeLookupOutcome.Ambiguous),
            "Two references DECLARING the same fully-qualified type is ambiguity, not absence. " +
            "Compilation.GetTypeByMetadataName answers null for both cases, so the resolver must " +
            "count declaring assemblies to tell them apart — that count is what Task 8 turns " +
            "into BL6023 instead of a misleading BL6016 'type not found' for a type that exists " +
            "twice. Fix NetTypeResolver, not the test.");
        Assert.That(result.Type, Is.Null,
            "an ambiguous reference has no single winner — picking one silently is how a build " +
            "ends up bound to whichever assembly happened to be enumerated first.");
        Assert.That(resolver.ResolveType("Contoso.Dup"), Is.Null,
            "ResolveType stays null-on-ambiguity; only ResolveTypeDetailed distinguishes.");
    }

    [Test]
    public void NestedTypeResolvesFromItsDottedSourceName()
    {
        // Metadata spells nested types Outer+Inner; BasicLang source has no '+' syntax and can
        // only ever spell them Outer.Inner, so the resolver must accept the dotted form or every
        // nested .NET type is unreachable.
        var probe = EmitProbeAssembly("BlnetNested",
            "namespace Contoso { public class Outer { public class Inner { public class Deep { } } } }");
        var resolver = NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { probe }));

        Assert.That(resolver.ResolveType("Contoso.Outer.Inner")?.FullName, Is.EqualTo("Contoso.Outer.Inner"),
            "a dotted nested-type name must resolve — the caller cannot know to write '+'. " +
            "Fix NetTypeResolver's candidate-name expansion.");
        Assert.That(resolver.ResolveType("Contoso.Outer.Inner.Deep")?.FullName,
            Is.EqualTo("Contoso.Outer.Inner.Deep"),
            "the fallback must be progressive, not a single rightmost-dot substitution.");
        Assert.That(resolver.ResolveType("Contoso.Outer+Inner")?.FullName, Is.EqualTo("Contoso.Outer.Inner"),
            "the raw metadata form must keep working for callers that already have it.");
    }

    [Test]
    public void GenericTypeRequiresItsMetadataArity_AndTheAritylessNameIsNotFound()
    {
        // A DOCUMENTED decision, pinned so it cannot become an accident. Arity is part of a .NET
        // generic type's identity: System.Func exists only as Func`1..Func`17, so guessing an
        // arity would fabricate a binding — the precise failure mode P2a-1 exists to remove.
        // Every caller that can reach here parsed `List(Of Integer)` and therefore knows the
        // arity. Task 5 owns generic call sites.
        var resolver = FrameworkOnly();

        Assert.That(resolver.ResolveType("System.Collections.Generic.List`1"), Is.Not.Null,
            "the metadata arity form must resolve.");
        Assert.That(resolver.ResolveTypeDetailed("System.Collections.Generic.List").Outcome,
            Is.EqualTo(NetTypeLookupOutcome.NotFound),
            "an arity-less generic name is NotFound BY DESIGN — the resolver never guesses an " +
            "arity. If this ever needs to change, change it deliberately here, and note that " +
            "System.Func has no zero-arity form to fall back on.");
    }

    [Test]
    public void NonPublicTypeResolvesButReportsItsAccessibility()
    {
        var probe = EmitProbeAssembly("BlnetHidden",
            "namespace Contoso { internal class Hidden { } public class Shown { } }");
        var resolver = NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { probe }));

        Assert.That(resolver.ResolveType("Contoso.Shown")!.IsPublic, Is.True);
        Assert.That(resolver.ResolveType("Contoso.Hidden")!.IsPublic, Is.False,
            "spec §6.1 makes accessibility part of the answer, so a non-public type resolves " +
            "with IsPublic false rather than vanishing. A caller that reports 'type not found' " +
            "for an internal type the user can plainly see in the source is unhelpful; the " +
            "caller decides the diagnostic, the resolver reports the fact.");
    }

    [Test]
    public void APublicTypeNestedInAnInternalOneIsNotEffectivelyPublic()
    {
        // DECLARED accessibility is public here; EFFECTIVE accessibility is not. Reporting true
        // hands the shim a type reference that fails in csc with CS0122 — the late-failure shape
        // P2a-1 exists to move earlier — so the containing chain has to be walked.
        var probe = EmitProbeAssembly("BlnetNestedAccess",
            "namespace Contoso { internal class HiddenOuter { public class VisibleInner { } } " +
            "public class ShownOuter { public class AlsoVisible { } } }");
        var resolver = NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { probe }));

        Assert.That(resolver.ResolveType("Contoso.ShownOuter.AlsoVisible")!.IsPublic, Is.True,
            "guard: public nested in public is reachable");
        Assert.That(resolver.ResolveType("Contoso.HiddenOuter.VisibleInner")!.IsPublic, Is.False,
            "IsPublic must be EFFECTIVE, not declared: a public type inside an internal one " +
            "cannot be named from outside its assembly. Walk ContainingType in " +
            "NetTypeResolver.IsEffectivelyPublic.");
    }

    // ------------------------------------------------------------------
    // Members.
    // ------------------------------------------------------------------

    [Test]
    public void EnumeratesInheritedMembers()
    {
        var members = FrameworkOnly().GetMembers("System.Text.StringBuilder").Select(m => m.Name).ToList();

        Assert.That(members, Does.Contain("Append"));
        Assert.That(members, Does.Contain("ToString"), "Inherited members must be included (spec §7.2).");
    }

    [Test]
    public void MembersComeFromTheWholeBaseChain_ExcludingSystemObject()
    {
        // StringBuilder.ToString is an OVERRIDE, so the framework assertion above passes even
        // with no base walk at all. This proves the walk, on a base type that cannot be confused
        // with an override, and pins the System.Object boundary spec §7.2 draws.
        var probe = EmitProbeAssembly("BlnetInherit",
            "namespace Contoso { public class Base { public void FromBase() { } " +
            "public virtual string Describe() => \"base\"; } " +
            "public class Derived : Base { public void FromDerived() { } " +
            "public override string Describe() => \"derived\"; } }");
        var resolver = NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { probe }));

        var members = resolver.GetMembers("Contoso.Derived");
        var names = members.Select(m => m.Name).ToList();

        Assert.That(names, Does.Contain("FromDerived"));
        Assert.That(names, Does.Contain("FromBase"),
            "GetMembers must walk base types (spec §7.2). Without the walk the shim can only " +
            "ever call members declared on the exact type named.");
        Assert.That(names, Does.Not.Contain("ReferenceEquals"),
            "spec §7.2: System.Object's own members are EXCLUDED (unless overridden, in which " +
            "case they appear as members of the overriding type). ReferenceEquals is static on " +
            "Object and can never be overridden, so it is the honest probe for that boundary. " +
            "If this fails the base walk no longer stops at System.Object.");
        Assert.That(members.Single(m => m.Name == "FromBase").DeclaringTypeFullName,
            Is.EqualTo("Contoso.Base"),
            "an inherited member reports the type that DECLARES it — §7.3 requires the mangler " +
            "to be collision-free over the fully-qualified declaring type, so reporting the " +
            "queried type here would collapse Base.FromBase and Derived.FromBase.");

        // Derived.Describe OVERRIDES Base.Describe, so a base walk without dedup reports it twice.
        // Single() is the assertion: it throws on two matches as loudly as on none.
        Assert.That(members.Single(m => m.Name == "Describe").DeclaringTypeFullName,
            Is.EqualTo("Contoso.Derived"),
            "an override must appear ONCE, under its MOST-DERIVED declaration — that is the one " +
            "that actually runs. Two entries here mean GetMembers lost its duplicate collapsing; " +
            "Task 5 would then see two identical candidates and answer Ambiguous for an ordinary " +
            "call. Fix NetTypeResolver.GetMembers, not the test.");
    }

    [Test]
    [TestCase("System.IO.FileStream")]
    [TestCase("System.IO.MemoryStream")]
    [TestCase("System.DayOfWeek")]
    [TestCase("System.Decimal")]
    [TestCase("System.Text.RegularExpressions.Regex")]
    [TestCase("System.Collections.Specialized.NameValueCollection")]
    public void NoTwoMembersSharePresentationIdentity(string typeName)
    {
        // §7.3 requires the mangler to be total over an overload set: "two overloads never
        // collide". That is unsatisfiable however the mangler is written if GetMembers itself
        // hands out two members with the same (kind, name, static-ness, parameter types).
        //
        // The cases are chosen so the BASE WALK ACTUALLY CONTRIBUTES. Asserting this on Regex
        // alone is VACUOUS — Regex's base is System.Object, which the walk excludes, so no
        // inherited member can collide with anything and the assertion holds trivially. FileStream
        // (26 colliding identities before the fix), MemoryStream (21) and any enum or struct
        // (3, Equals/GetHashCode/ToString from Enum/ValueType) are the cases with teeth.
        var members = FrameworkOnly().GetMembers(typeName);
        Assert.That(members, Is.Not.Empty, "guard: the fixture must have found a real type");

        var collisions = members
            .GroupBy(m => (m.Kind, m.Name, m.IsStatic, m.Arity,
                           Params: string.Join(",", m.Parameters)))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.Kind} {g.Key.Name}`{g.Key.Arity}({g.Key.Params}) declared by "
                         + string.Join(" AND ", g.Select(m => m.DeclaringTypeFullName)))
            .ToList();

        Assert.That(collisions, Is.Empty,
            "two members of " + typeName + " present the same identity. This is what an " +
            "un-deduplicated base walk produces: the override AND the member it overrides. " +
            "Consequences: Task 5 matches by name and parameter types, gets two candidates and " +
            "reports Ambiguous -> a spurious BL6018 on an ordinary call; Task 12 emits two proxy " +
            "slots for one member. Fix the collapsing in NetTypeResolver.GetMembers.\n"
            + string.Join("\n", collisions));
    }

    // ------------------------------------------------------------------
    // The OTHER half of the collapse: it must not DELETE anything. The test above cannot
    // see that — it groups by the very key GetMembers collapses on, so it holds no matter
    // what the collapse destroys. These are the tests with an INDEPENDENT oracle.
    // ------------------------------------------------------------------

    /// <summary>
    /// Counts members the way a CLR signature actually distinguishes them, derived straight from
    /// Roslyn and deliberately INDEPENDENT of NetTypeResolver's own key — that independence is the
    /// entire point. Parameter types are spelled with Roslyn's raw display format rather than the
    /// resolver's normalized one: the two disagree on <c>nint</c> vs <c>System.IntPtr</c>, but both
    /// are injective over distinct types, so the DISTINCT COUNT is the same and the comparison
    /// isolates the axes under test (kind, name, static-ness, arity, parameter ref-kinds/types).
    /// </summary>
    private static int SignatureCompleteMemberCount(string typeName)
    {
        var format = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

        var compilation = CSharpCompilation.Create("oracle",
            references: NetTypeResolverTestRefs.FrameworkPaths.Select(p => MetadataReference.CreateFromFile(p)));
        var type = compilation.GetTypeByMetadataName(typeName);
        Assert.That(type, Is.Not.Null, "guard: the oracle must find " + typeName);

        static IEnumerable<IParameterSymbol> Parameters(ISymbol m) => m switch
        {
            IMethodSymbol method => method.Parameters,
            IPropertySymbol property => property.Parameters,
            _ => Enumerable.Empty<IParameterSymbol>(),
        };

        var keys = new HashSet<string>();
        var isQueriedType = true;
        for (var t = type; t != null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
        {
            foreach (var m in t.GetMembers())
            {
                if (m.DeclaredAccessibility != Accessibility.Public || m.IsImplicitlyDeclared)
                    continue;
                if (m is IMethodSymbol method)
                {
                    if (method.MethodKind != MethodKind.Ordinary
                        && method.MethodKind != MethodKind.Constructor)
                        continue;
                    if (!isQueriedType && method.MethodKind == MethodKind.Constructor)
                        continue;   // constructors are not inherited
                }
                else if (!(m is IPropertySymbol || m is IFieldSymbol))
                {
                    continue;
                }

                var kind = m is IMethodSymbol me
                    ? (me.MethodKind == MethodKind.Constructor ? "Ctor" : "Method")
                    : m is IPropertySymbol ? "Property" : "Field";
                var arity = m is IMethodSymbol ma ? ma.Arity : 0;
                var ps = string.Join(",", Parameters(m).Select(p =>
                    (p.RefKind == RefKind.None ? "" : p.RefKind + " ") + p.Type.ToDisplayString(format)));
                keys.Add($"{kind}|{m.MetadataName}|{m.IsStatic}|{arity}|{ps}");
            }
            isQueriedType = false;
        }
        return keys.Count;
    }

    [Test]
    [TestCase("System.Threading.Tasks.Task")]
    [TestCase("System.Linq.Expressions.Expression")]
    [TestCase("System.Linq.IQueryProvider")]
    [TestCase("System.Diagnostics.Tracing.EventSource")]
    public void TheDuplicateCollapseLosesNoMember(string typeName)
    {
        // The four types measured to lose members when the collapse key is not a COMPLETE CLR
        // signature. A deleted overload is strictly worse than a duplicated one: duplicates make
        // Task 5 answer Ambiguous, which is at least a signal, whereas a deleted overload makes it
        // answer "no such overload" for a member that plainly exists.
        //
        //   arity missing    -> Task 99->97, Expression 316->310, IQueryProvider 4->2
        //                       (Task.FromException vs FromException<T>, all six
        //                        Expression.Lambda/Lambda<TDelegate> pairs, both CreateQuery)
        //   ref-kind missing -> EventSource 28->27
        //                       (Write(String, EventSourceOptions, T) vs
        //                        Write(String, ref EventSourceOptions, ref T))
        Assert.That(FrameworkOnly().GetMembers(typeName).Count,
            Is.EqualTo(SignatureCompleteMemberCount(typeName)),
            "GetMembers returned fewer members than " + typeName + " has distinct CLR " +
            "signatures, so the duplicate collapse DELETED a real overload. Its key must carry " +
            "every axis of a signature — kind, name, static-ness, generic ARITY, and each " +
            "parameter's REF-KIND and type. Add the missing axis to NetMemberDescriptor and to " +
            "NetTypeResolver.SignatureKey; do not relax this test.");
    }

    [Test]
    public void GenericAndNonGenericOverloadsBothSurvive()
    {
        // The named case behind the count above, so a failure says WHICH member vanished.
        var fromException = FrameworkOnly().GetMembers("System.Threading.Tasks.Task")
            .Where(m => m.Name == "FromException" && m.Kind == NetMemberCategory.Method)
            .ToList();

        Assert.That(fromException.Select(m => m.Arity), Is.EquivalentTo(new[] { 0, 1 }),
            "Task.FromException(Exception) and Task.FromException<T>(Exception) differ ONLY by " +
            "generic arity — IMethodSymbol.MetadataName carries no arity suffix, so a key built " +
            "from name and parameter types alone cannot tell them apart and deletes one.");
    }

    [Test]
    public void OverloadsDifferingOnlyByRefKindBothSurvive()
    {
        var write = FrameworkOnly().GetMembers("System.Diagnostics.Tracing.EventSource")
            .Where(m => m.Name == "Write" && m.Arity == 1 && m.Parameters.Count == 3)
            .ToList();

        Assert.That(write.Select(m => string.Join(",", m.Parameters.Select(p => p.RefKind))),
            Is.EquivalentTo(new[] { "None,None,None", "None,Ref,Ref" }),
            "EventSource.Write(String, EventSourceOptions, T) and " +
            "Write(String, ref EventSourceOptions, ref T) differ ONLY by parameter ref-kind. " +
            "Recording p.Type without p.RefKind makes them one member and metadata-table order " +
            "decides which survives.");
    }

    [Test]
    public void ConstructorsAreNotInherited()
    {
        // Constructors are the one member kind that is NOT inherited: `New Derived(baseCtorArgs)`
        // is a compile error unless Derived declares that signature. Collecting them from base
        // types invents members no caller can invoke — Task 5 would resolve one and Task 12 would
        // emit a proxy slot for it. Measured on the framework, it also added a spurious member to
        // FileNotFoundException and ArgumentNullException among others.
        var probe = EmitProbeAssembly("BlnetCtors",
            "namespace Contoso { public class CtorBase { public CtorBase(int i) { } } " +
            "public class CtorDerived : CtorBase { public CtorDerived(string s) : base(0) { } } }");
        var resolver = NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { probe }));

        var ctors = resolver.GetMembers("Contoso.CtorDerived")
            .Where(m => m.Kind == NetMemberCategory.Constructor).ToList();

        Assert.That(ctors.Select(m => string.Join(",", m.ParameterTypeFullNames)),
            Is.EqualTo(new[] { "System.String" }),
            "only CtorDerived's OWN constructor may appear. CtorBase(int) is not callable as a " +
            "constructor of CtorDerived, so the base walk must skip constructors.");
    }

    [Test]
    [TestCase("System.IntPtr", "System.IntPtr")]
    [TestCase("System.UIntPtr", "System.UIntPtr")]
    public void NativeIntegerTypesAreNotSpelledWithTheirCSharpKeyword(string metadataName, string expected)
    {
        // ToDisplayString renders these as nint/nuint even with UseSpecialTypes off — measured at
        // 670 and 274 occurrences across the public framework's parameter/return spellings. Two
        // spellings of one type mangle to two export names (§7.3), and would also split the
        // signature key.
        Assert.That(FrameworkOnly().ResolveType(metadataName)!.FullName, Is.EqualTo(expected),
            "NetTypeResolver.TypeName must special-case SpecialType.System_IntPtr/System_UIntPtr; " +
            "a bare ToDisplayString gives the C# keyword.");
    }

    [Test]
    public void TupleTypesAreSpelledStructurally_NotAsTupleSyntaxWithElementNames()
    {
        // Math.SinCos returns (double Sin, double Cos). ToDisplayString spells that as tuple
        // SYNTAX INCLUDING THE ELEMENT NAMES, so the same runtime type gets a different name per
        // API, and the string carries '(', ')', ',' and spaces straight into the mangler.
        var sinCos = FrameworkOnly().GetMembers("System.Math").Single(m => m.Name == "SinCos");

        Assert.That(sinCos.TypeFullName,
            Is.EqualTo("System.ValueTuple<System.Double, System.Double>"),
            "a tuple must be reported as its underlying ValueTuple<...>. Element names are " +
            "metadata decoration, not part of the type — two APIs over the same tuple type must " +
            "produce the same TypeFullName.");
        Assert.That(sinCos.TypeFullName, Does.Not.Contain("Sin").And.Not.Contain("Cos"));
    }

    [Test]
    public void AnOverrideIsReportedOnceUnderItsMostDerivedDeclaration()
    {
        // The concrete, named case behind the invariant above: Stream declares
        // Read(Byte[], Int32, Int32) and FileStream overrides it.
        var read = FrameworkOnly().GetMembers("System.IO.FileStream")
            .Where(m => m.Kind == NetMemberCategory.Method && m.Name == "Read"
                        && string.Join(",", m.ParameterTypeFullNames)
                           == "System.Byte[],System.Int32,System.Int32")
            .ToList();

        Assert.That(read.Count, Is.EqualTo(1),
            "FileStream.Read(Byte[], Int32, Int32) must appear exactly once, not once per " +
            "declaring type in the chain.");
        Assert.That(read[0].DeclaringTypeFullName, Is.EqualTo("System.IO.FileStream"),
            "the MOST-DERIVED declaration must win — it is the implementation that actually " +
            "runs, and the one a caller means. Reporting System.IO.Stream here would send Task " +
            "12's proxy at the base slot.");
    }

    [Test]
    public void TwoIndexersBothSurviveTheDuplicateCollapse()
    {
        // The regression the duplicate collapse could have introduced. An indexer is a
        // PARAMETERIZED property, so recording property parameters is what keeps its identity
        // distinct. NameValueCollection declares this[int] and this[string]; treating property
        // parameters as always-empty makes those one identity and silently drops one.
        var indexers = FrameworkOnly()
            .GetMembers("System.Collections.Specialized.NameValueCollection")
            .Where(m => m.Kind == NetMemberCategory.Property && m.Name == "Item")
            .ToList();

        Assert.That(indexers.Select(m => string.Join(",", m.ParameterTypeFullNames)),
            Is.EquivalentTo(new[] { "System.String", "System.Int32" }),
            "both indexers must survive, each carrying its own parameter type. If one is " +
            "missing, NetTypeResolver stopped recording IPropertySymbol.Parameters and the " +
            "duplicate collapse ate it — an overload silently vanishing from the surface.");
    }

    [Test]
    public void MemberCarriesTheThreeInputsTheManglerNeeds()
    {
        // §7.3: NetNameMangler produces its identifier from (declaring type, member name,
        // parameter types) — plus the arity and ref-kinds those three do not capture. All of it
        // must be readable off a NetMemberDescriptor.
        var overloads = FrameworkOnly().GetMembers("System.Text.RegularExpressions.Regex")
            .Where(m => m.Name == "IsMatch" && m.Kind == NetMemberCategory.Method)
            .ToList();

        var instanceOneArg = overloads.Single(m =>
            !m.IsStatic && m.Parameters.Count == 1 &&
            m.Parameters[0].TypeFullName == "System.String");

        Assert.That(instanceOneArg.DeclaringTypeFullName,
            Is.EqualTo("System.Text.RegularExpressions.Regex"));
        Assert.That(instanceOneArg.Arity, Is.EqualTo(0));
        Assert.That(instanceOneArg.Parameters[0].RefKind, Is.EqualTo(NetRefKind.None));
        Assert.That(instanceOneArg.TypeFullName, Is.EqualTo("System.Boolean"),
            "parameter and return type names must be fully qualified and NOT C# keywords — " +
            "'bool' and 'System.Boolean' would mangle to different identifiers for one method.");
        Assert.That(overloads.Any(m => m.IsStatic), Is.True,
            "the static overload set must be present too; Regex.IsMatch(String, String) is static.");

        // §7.3's "two overloads never collide" is asserted by NoTwoMembersSharePresentationIdentity
        // across types whose base walk actually contributes. Asserting it HERE, on Regex, would be
        // vacuous: Regex derives directly from System.Object, which the walk excludes, so no
        // inherited member can collide and the assertion would hold no matter what GetMembers did.
    }

    [Test]
    public void ConstructorsAreEnumeratedUnderTheirMetadataName()
    {
        var ctors = FrameworkOnly().GetMembers("System.Text.RegularExpressions.Regex")
            .Where(m => m.Kind == NetMemberCategory.Constructor).ToList();

        Assert.That(ctors, Is.Not.Empty,
            "spec §7.2 includes public constructors — without them `New Regex(\"a\")` has no " +
            "target to resolve against.");
        Assert.That(ctors.Select(m => m.Name).Distinct(), Is.EqualTo(new[] { ".ctor" }),
            "constructors carry their metadata name '.ctor'; Kind is what distinguishes them. " +
            "If this changes, Task 9's mangler must change with it.");
    }

    [Test]
    public void PropertyAccessorsAreNotReportedAsMethods()
    {
        var members = FrameworkOnly().GetMembers("System.Text.StringBuilder");

        Assert.That(members.Select(m => m.Name), Does.Contain("Length"));
        Assert.That(members.Single(m => m.Name == "Length").Kind, Is.EqualTo(NetMemberCategory.Property));
        Assert.That(members.Select(m => m.Name), Does.Not.Contain("get_Length"),
            "a property is ONE member, not a property plus two synthesized accessor methods — " +
            "otherwise every property produces three shim exports and three proxy slots.");
    }

    [Test]
    public void GetMembersOnAnUnknownTypeIsEmpty_NotAThrow()
    {
        Assert.That(FrameworkOnly().GetMembers("System.Text.Rejex"), Is.Empty,
            "the resolver is called from the analyzer and the IntelliSense path; throwing on an " +
            "unknown type would turn a user typo into a crashed build or a dead LSP request.");
    }

    // ------------------------------------------------------------------
    // The §6.2 property that makes this a correctness fix, not a convenience.
    // ------------------------------------------------------------------

    [Test]
    public void DoesNotLoadAssembliesIntoTheProcess()
    {
        // The warm-up is load-bearing: the FIRST Roslyn call anywhere in this process JITs and
        // loads Microsoft.CodeAnalysis*, and NUnit gives no ordering guarantee about whether some
        // other fixture already did that. Without it this test fails or passes by test order.
        FrameworkOnly().ResolveType("System.Text.RegularExpressions.Regex");

        var before = AppDomain.CurrentDomain.GetAssemblies().Length;
        FrameworkOnly().ResolveType("System.Text.RegularExpressions.Regex");
        Assert.That(AppDomain.CurrentDomain.GetAssemblies().Length, Is.EqualTo(before),
            "MetadataReference.CreateFromFile reads metadata without loading. If this fails the " +
            "resolver has regressed to Assembly.LoadFrom semantics (file locks, module " +
            "initializers, no unload) — the very defect spec §6.2 exists to remove.");
    }

    [Test]
    public void ResolvingFromAnOnDiskAssemblyDoesNotLoadThatAssembly()
    {
        // The count check above can only ever say "nothing NEW was loaded", and every framework
        // assembly it reads is already loaded in this host — so it cannot distinguish reading
        // from loading. This one can: the probe assembly's simple name is unique to this test
        // run, so its presence in the process is unambiguous evidence of a load.
        var name = "BlnetLoadProbe" + Guid.NewGuid().ToString("N");
        var probe = EmitProbeAssembly(name, "namespace Contoso { public class LoadProbe { } }");

        var resolver = NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { probe }));
        Assert.That(resolver.ResolveType("Contoso.LoadProbe"), Is.Not.Null, "guard: the probe must resolve");

        Assert.That(AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name),
            Does.Not.Contain(name),
            "the resolver READ this assembly's metadata and must not have LOADED it. A load is " +
            "what spec §6.2 removes: it locks the file, runs module initializers, and cannot be " +
            "undone — which is why the LSP could not see a rebuilt referenced assembly without " +
            "restarting.");
    }

    [Test]
    public void UnreadableReferenceIsSkippedWithBL6021_NotAnException()
    {
        // Every path here is user-reachable. A <HintPath> can point at a native DLL, at a
        // truncated file, or at something deleted between resolution and use; the framework
        // directory itself is full of native DLLs. MetadataReference.CreateFromFile throws
        // FileNotFoundException for the missing case and defers BadImageFormatException for the
        // others, so an unguarded resolver either crashes the build or silently degrades.
        var junk = Path.Combine(_dir, "Junk.dll");
        File.WriteAllBytes(junk, new byte[] { 0x4D, 0x5A });        // "MZ" and nothing else
        var missing = Path.Combine(_dir, "NeverExisted.dll");

        var bad = new List<string> { junk, missing };
        var nativeDll = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!, "coreclr.dll");
        if (File.Exists(nativeDll))
            bad.Add(nativeDll);

        NetTypeResolver resolver = null!;
        Assert.DoesNotThrow(
            () => resolver = NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths.Concat(bad)),
            "an unreadable reference must never escape as an exception — this runs on the build " +
            "path AND the IntelliSense path.");

        Assert.That(resolver.ResolveType("System.Text.RegularExpressions.Regex"), Is.Not.Null,
            "one bad reference must not disable resolution of everything else.");

        Assert.That(resolver.Diagnostics.Select(d => d.Code).Distinct(), Is.EqualTo(new[] { "BL6021" }),
            "an unreadable reference IS a reference that could not be resolved (spec §11.4's " +
            "BL6021). Dropping it silently is exactly what BL6021 replaced on the native path.");
        Assert.That(resolver.Diagnostics.Count, Is.EqualTo(bad.Count),
            "one diagnostic per unreadable reference, so the message can name the file.");
        Assert.That(string.Join("\n", resolver.Diagnostics.Select(d => d.Message)),
            Does.Contain("Junk.dll").And.Contain("NeverExisted.dll"));
    }

    // ------------------------------------------------------------------
    // Overload resolution — spec §6.1's THIRD question, "given this call site with these
    // argument types, which overload wins?", and the whole reason §6.1 chose Roslyn over
    // hand-rolled metadata reading.
    //
    // HOW IT WORKS, because the tests only make sense against the mechanism: there is no public
    // Roslyn API that resolves an overload from a bag of type symbols. So NetTypeResolver
    // SYNTHESIZES a C# fragment that makes the call and asks the SemanticModel who won. That is
    // not an approximation of C# overload resolution — it IS C# overload resolution, which is why
    // widening, params, optional parameters and generic inference below are pinned as *facts about
    // C#* rather than as behavior anyone implemented here.
    //
    // ORACLE INDEPENDENCE: every expected value below is what a C# compiler does with the same
    // call — checkable by writing the C# — and none of it is derived from NetTypeResolver's own
    // logic. That distinction is why these tests can fail; see TheDuplicateCollapseLosesNoMember
    // for what happens when a test's oracle is the implementation's own key.
    // ------------------------------------------------------------------

    private static NetOverloadResult Overload(
        NetTypeResolver resolver, string type, NetCallForm form, string member, params string[] args) =>
        resolver.ResolveOverload(type, form, member, args);

    [Test]
    public void ResolveOverload_SelectsByArity()
    {
        var r = FrameworkOnly();

        var one = Overload(r, "System.Text.RegularExpressions.Regex", NetCallForm.Instance, "IsMatch",
                           "System.String");
        var two = Overload(r, "System.Text.RegularExpressions.Regex", NetCallForm.Static, "IsMatch",
                           "System.String", "System.String");

        Assert.That(one.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "spec §6.1 question 3: Regex.IsMatch(String) is an overload that exists and must be " +
            "selected. Fix NetTypeResolver.ResolveOverload.");
        Assert.That(string.Join(",", one.Member!.ParameterTypeFullNames), Is.EqualTo("System.String"),
            "the ONE-argument overload must be the winner; any other parameter list means arity " +
            "was not what selected it.");
        Assert.That(one.Member.IsStatic, Is.False,
            "Regex.IsMatch(String) is the INSTANCE overload — a static winner here means the call " +
            "form was ignored, and Task 12 would emit a static export for an instance call.");

        Assert.That(two.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "spec §6.1 question 3: the two-argument static Regex.IsMatch(String, String) exists.");
        Assert.That(string.Join(",", two.Member!.ParameterTypeFullNames),
            Is.EqualTo("System.String,System.String"),
            "spec §6.1: two overloads of the same name differing in ARITY must be told apart by " +
            "the argument count, not by first-match.");
        Assert.That(two.Member.IsStatic, Is.True,
            "Regex.IsMatch(String, String) is Shared; a non-static winner means the static call " +
            "form selected an instance member.");
    }

    [Test]
    public void ResolveOverload_SelectsBetweenSameArityOverloadsByParameterType()
    {
        // Regex declares INSTANCE IsMatch(String, Int32) and IsMatch(ReadOnlySpan<Char>, Int32).
        // Same name, same arity — only the first parameter's type separates them, which is
        // exactly the discrimination spec §6.1 says cannot be approximated.
        var m = Overload(FrameworkOnly(), "System.Text.RegularExpressions.Regex",
                         NetCallForm.Instance, "IsMatch", "System.String", "System.Int32");

        Assert.That(m.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "spec §6.1 question 3: the instance Regex.IsMatch(String, Int32) exists, so a " +
            "same-arity overload set must not defeat resolution.");
        Assert.That(string.Join(",", m.Member!.ParameterTypeFullNames),
            Is.EqualTo("System.String,System.Int32"),
            "spec §6.1 question 3: with a String first argument the String overload wins, not the " +
            "ReadOnlySpan<Char> one. If this picked the Span overload, §8.3 would then report " +
            "BL6019 'not marshalable' for a call that is perfectly marshalable.");
    }

    [Test]
    public void ResolveOverload_ResolvesAnInheritedOverload()
    {
        // CopyTo(Stream) is declared on System.IO.Stream and NOT overridden by FileStream, so it
        // can only be found through spec §7.2's base walk. Resolution must see the whole chain.
        var m = Overload(FrameworkOnly(), "System.IO.FileStream", NetCallForm.Instance, "CopyTo",
                         "System.IO.Stream");

        Assert.That(m.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "spec §7.2: members come from the type AND its base types, so an inherited overload " +
            "must resolve. Fix NetTypeResolver.ResolveOverload's candidate source.");
        Assert.That(m.Member!.DeclaringTypeFullName, Is.EqualTo("System.IO.Stream"),
            "spec §7.3 mangles from the DECLARING type — reporting FileStream here would name an " +
            "export for a method FileStream does not declare.");
    }

    [Test]
    public void ResolveOverload_ResolvesAGenericMethodByInference()
    {
        // Task.FromResult<TResult>(TResult) has no non-generic sibling, so this can only resolve
        // if TResult is INFERRED from the argument. Task.FromException is the opposite case and is
        // covered by GenericAndNonGenericOverloadsBothSurvive.
        var m = Overload(FrameworkOnly(), "System.Threading.Tasks.Task", NetCallForm.Static,
                         "FromResult", "System.Int32");

        Assert.That(m.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "spec §6.1: overload resolution 'with generics' is named explicitly. A matcher that " +
            "compares parameter type SPELLINGS cannot resolve this — the parameter spells 'TResult' " +
            "and the argument spells 'System.Int32'. Fix NetTypeResolver.ResolveOverload.");
        Assert.That(m.Member!.Arity, Is.EqualTo(1),
            "the GENERIC definition must be the reported member; arity 0 would mean a different " +
            "method was selected.");
        Assert.That(m.Member.Name, Is.EqualTo("FromResult"),
            "a different member name means inference selected something other than the method " +
            "asked for.");
    }

    [Test]
    public void ResolveOverload_AmbiguityIsADistinctOutcome_NotNullAndNotNoMatch()
    {
        // A GENUINE C# ambiguity, built rather than borrowed so it cannot drift with the
        // framework: F(int,long) and F(long,int) called with (int,int) — neither is better, which
        // is CS0121. The oracle is the C# specification's betterness rule, not this resolver.
        var probe = EmitProbeAssembly("BlnetAmbOverload",
            "namespace Contoso { public class Amb { " +
            "public void F(int a, long b) { } public void F(long a, int b) { } } }");
        var r = NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { probe }));

        var m = Overload(r, "Contoso.Amb", NetCallForm.Instance, "F", "System.Int32", "System.Int32");

        Assert.That(m.Outcome, Is.EqualTo(NetOverloadOutcome.Ambiguous),
            "spec §11.4 gives ambiguity its own code — BL6018 'ambiguous overload' — separate " +
            "from BL6017 'no matching overload'. Collapsing the two here makes one of those " +
            "diagnostics a lie in Task 8: 'no such overload' for a call the user can see two " +
            "candidates for is the least actionable message the compiler could produce. Fix " +
            "NetTypeResolver, not the test.");
        Assert.That(m.Member, Is.Null,
            "an ambiguous call has no winner; naming one would send Task 12's proxy at whichever " +
            "candidate happened to be enumerated first.");
    }

    [Test]
    public void ResolveOverload_NoMatchIsADistinctOutcome()
    {
        var m = Overload(FrameworkOnly(), "System.Text.RegularExpressions.Regex",
                         NetCallForm.Constructor, ".ctor",
                         "System.Int32", "System.Int32", "System.Int32");

        Assert.That(m.Outcome, Is.EqualTo(NetOverloadOutcome.NoMatch),
            "`New Regex(1, 2, 3)` is the exact program spec §6.1 opens with: before P2a-1 it " +
            "type-checked clean and failed inside csc. NoMatch is what becomes BL6017 (§11.4).");
        Assert.That(m.Member, Is.Null,
            "a NoMatch must carry NO member. A non-null member alongside NoMatch would hand Task 12 " +
            "a bogus export for a constructor that does not exist — worse than the diagnostic.");
    }

    [Test]
    public void ResolveOverload_WideningResolvesRatherThanReportingNoMatch()
    {
        // THE test that decides whether Task 8 warns on VALID programs. `Console.WriteLine(s)`
        // with a Short, and `Math.Abs(b)` with a Byte, are ordinary BasicLang. An exact-match
        // matcher answers NoMatch for both -> BL6017 on code that compiles fine today, which
        // spec §6.3's "valid programs behave identically on both backends" forbids.
        //
        // The expected WINNERS are C# facts, independently checkable: Short widens to Int32 and
        // Console has no Int16 overload; Byte widens to Int16 and Math.Abs(Int16) is BETTER than
        // Math.Abs(Int32) because Int16 is the closer target. Note the second one is not
        // "whatever's closest by hand" — it is C# §12.6.4 betterness, which is the reason this
        // resolver delegates to Roslyn instead of ranking candidates itself.
        var r = FrameworkOnly();

        var writeLine = Overload(r, "System.Console", NetCallForm.Static, "WriteLine", "System.Int16");
        Assert.That(writeLine.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "spec §6.3: an implicit widening conversion must RESOLVE. NoMatch here is a BL6017 " +
            "warning on a program that compiles clean on the C# backend today.");
        Assert.That(string.Join(",", writeLine.Member!.ParameterTypeFullNames),
            Is.EqualTo("System.Int32"),
            "Console has no Int16 overload, so C# widens to Int32.");

        var abs = Overload(r, "System.Math", NetCallForm.Static, "Abs", "System.Byte");
        Assert.That(abs.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "spec §6.3: Byte widens to several Math.Abs parameter types, so this resolves in C# and " +
            "must resolve here.");
        Assert.That(string.Join(",", abs.Member!.ParameterTypeFullNames), Is.EqualTo("System.Int16"),
            "C# betterness picks Abs(Int16) over Abs(Int32)/Abs(Int64) for a Byte argument. Any " +
            "hand-rolled candidate ranking that gets this wrong selects a different member and " +
            "Task 12 emits the wrong export — which is worse than a warning.");
    }

    [Test]
    public void ResolveOverload_ParamsExpansionResolves()
    {
        // String.Concat has fixed 2-, 3- and 4-argument overloads and then `params String[]`.
        // Five arguments can only bind through the params expansion, which spec §6.1 names.
        var m = Overload(FrameworkOnly(), "System.String", NetCallForm.Static, "Concat",
                         "System.String", "System.String", "System.String", "System.String",
                         "System.String");

        Assert.That(m.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "spec §6.1 lists `params` among the things resolution must handle. Fix " +
            "NetTypeResolver.ResolveOverload.");
        Assert.That(string.Join(",", m.Member!.ParameterTypeFullNames), Is.EqualTo("System.String[]"),
            "the params array overload is the winner, so the reported member has ONE parameter of " +
            "array type — not five.");
    }

    [Test]
    public void ResolveOverload_OptionalParametersResolve()
    {
        var probe = EmitProbeAssembly("BlnetOptional",
            "namespace Contoso { public class Opt { public void F(int a, int b = 5, string c = null) { } } }");
        var r = NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { probe }));

        var m = Overload(r, "Contoso.Opt", NetCallForm.Instance, "F", "System.Int32");

        Assert.That(m.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "spec §6.1 lists optional parameters among the things resolution must handle. A " +
            "strict arity comparison answers NoMatch and Task 8 warns on a valid call.");
        Assert.That(m.Member!.Parameters.Count, Is.EqualTo(3),
            "the member's real signature is reported, not the trimmed call-site shape — Task 12 " +
            "has to emit a proxy over the DECLARED parameters.");
    }

    [Test]
    public void ResolveOverload_ByRefParameterResolvesWithoutACallSiteKeyword()
    {
        // BasicLang has no call-site ByRef keyword — `Integer.TryParse(s, n)` is how the language
        // spells it — but C# REQUIRES `out` at the call site (CS1620). The synthesized probe must
        // therefore supply the keyword the candidate declares, taken from
        // NetParameterDescriptor.RefKind. Without that, every ref/out member in the framework is
        // unresolvable, and spec §8.3's `ref`/`out` pointer-slot row has nothing to emit for.
        var m = Overload(FrameworkOnly(), "System.Int32", NetCallForm.Static, "TryParse",
                         "System.String", "System.Int32");

        Assert.That(m.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "Int32.TryParse(String, ByRef Integer) must resolve. If this is NoMatch, " +
            "ResolveOverload stopped deriving call-site ref/out keywords from the candidates' " +
            "NetParameterDescriptor.RefKind — see spec §8.3's last row.");
        Assert.That(m.Member!.Parameters.Select(p => p.RefKind),
            Is.EqualTo(new[] { NetRefKind.None, NetRefKind.Out }),
            "the winner is TryParse(String, out Int32) — the ref-kinds are part of what Task 12 " +
            "needs to emit a pointer slot.");
    }

    [Test]
    public void ResolveOverload_SelectsAConstructor()
    {
        var m = Overload(FrameworkOnly(), "System.Text.RegularExpressions.Regex",
                         NetCallForm.Constructor, ".ctor",
                         "System.String", "System.Text.RegularExpressions.RegexOptions");

        Assert.That(m.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "spec §7.2 admits public constructors, and `New Regex(p, opts)` needs one selected.");
        Assert.That(m.Member!.Kind, Is.EqualTo(NetMemberCategory.Constructor),
            "the Constructor call form must select a Constructor, not a same-named method.");
        Assert.That(m.Member.Name, Is.EqualTo(".ctor"),
            "constructors carry the metadata name '.ctor' — Task 9's mangler depends on it.");
        Assert.That(string.Join(",", m.Member.ParameterTypeFullNames),
            Is.EqualTo("System.String,System.Text.RegularExpressions.RegexOptions"));
    }

    [Test]
    public void ResolveOverload_OnAGenericTypeRequiresItsTypeArguments()
    {
        // Queue(Of Integer) is an UNCLAIMED generic (spec §6.5 row (b) claims List/Dictionary/
        // HashSet/Task/Func/Action, not Queue), so it really does reach the resolver. Its
        // Enqueue(T) parameter spells 'T', so the type arguments are what make the call
        // resolvable at all — and consistent with ResolveType, they are REQUIRED, never guessed.
        var r = FrameworkOnly();

        var withArgs = r.ResolveOverload("System.Collections.Generic.Queue`1", NetCallForm.Instance,
            "Enqueue", new[] { "System.Int32" }, new[] { "System.Int32" });
        Assert.That(withArgs.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "Queue(Of Integer).Enqueue(1) must resolve. Its parameter is the type parameter T, " +
            "so a spelling comparison against 'System.Int32' can never match — the receiver has " +
            "to be CONSTRUCTED before resolution. Fix NetTypeResolver.ResolveOverload.");
        Assert.That(withArgs.Member!.Name, Is.EqualTo("Enqueue"),
            "the winner must be Enqueue itself; another name means substitution selected a " +
            "different member of the constructed type.");

        var withoutArgs = Overload(r, "System.Collections.Generic.Queue`1", NetCallForm.Instance,
            "Enqueue", "System.Int32");
        Assert.That(withoutArgs.Outcome, Is.EqualTo(NetOverloadOutcome.NoMatch),
            "A DOCUMENTED decision, pinned so it cannot become an accident: type arguments are " +
            "required and never guessed, exactly as generic ARITY is for ResolveType. Guessing " +
            "'Object' would fabricate a binding, which is the failure P2a-1 exists to remove. " +
            "Task 8 has the arguments — it parsed `Queue(Of Integer)`.");
    }

    [Test]
    public void ResolveOverload_AnOverriddenMemberIsResolved_NotReportedAmbiguous()
    {
        // The plan's named hazard. Stream declares Read(Byte[], Int32, Int32) and FileStream
        // overrides it, so an un-deduplicated candidate walk offers TWO identical candidates and
        // reports Ambiguous — a spurious BL6018 on an ordinary `fs.Read(buf, 0, n)`. The dedup
        // lives in exactly one place (CandidateMembers); this proves resolution consumes it.
        var m = Overload(FrameworkOnly(), "System.IO.FileStream", NetCallForm.Instance, "Read",
                         "System.Byte[]", "System.Int32", "System.Int32");

        Assert.That(m.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "an override plus the member it overrides is ONE member, not an ambiguity. Ambiguous " +
            "here means ResolveOverload re-walked derived->base itself instead of consuming " +
            "CandidateMembers, and Task 8 would emit BL6018 on `fs.Read(buf, 0, n)`.");
        Assert.That(m.Member!.DeclaringTypeFullName, Is.EqualTo("System.IO.FileStream"),
            "the MOST-DERIVED declaration wins — it is the implementation that runs.");
    }

    [Test]
    public void ResolveOverload_ASharedMemberCalledThroughAnInstanceResolves()
    {
        // VB, and therefore BasicLang, permits `obj.SharedMethod()`; C# does not (CS0176). The
        // resolver must not inherit C#'s stricter rule, or `r.IsMatch(input, pattern)` on a Regex
        // variable becomes a BL6017 on a valid program.
        var m = Overload(FrameworkOnly(), "System.Text.RegularExpressions.Regex",
                         NetCallForm.Instance, "IsMatch", "System.String", "System.String");

        Assert.That(m.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "a Shared member reached through an instance receiver must resolve — VB allows it, so " +
            "spec §6.3's equal-behavior claim requires it. If this is NoMatch, ResolveOverload " +
            "lost its CS0176 fallback from the instance form to the static form.");
        Assert.That(m.Member!.IsStatic, Is.True,
            "the winner is the SHARED overload — that is the whole point of the fallback. A " +
            "non-static winner would mean an instance overload was selected for a 2-argument call " +
            "that only the static set can take.");
    }

    [Test]
    public void ResolveOverload_OnAStaticClassResolvesFromEitherCallForm()
    {
        var r = FrameworkOnly();

        Assert.That(Overload(r, "System.Console", NetCallForm.Static, "WriteLine", "System.String")
                    .Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "Console.WriteLine is the single most common .NET call in the corpus. (It is CLAIMED " +
            "by spec §6.5 row (c) and so never reaches the resolver in practice — but a resolver " +
            "that cannot answer for a static class is broken for Math, Convert and Path too.)");

        Assert.That(Overload(r, "System.Console", NetCallForm.Instance, "WriteLine", "System.String")
                    .Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "a static class cannot be spelled as a parameter type in C# (CS0721), so the instance " +
            "call form has to fall back to the static one rather than answering NoMatch.");
    }

    [Test]
    public void ResolveOverload_AMemberOutsideTheSection72SurfaceIsNoMatch()
    {
        // Regex does not override Equals, so the only Equals(Object) in scope is System.Object's —
        // which spec §7.2 EXCLUDES from the surface. NoMatch here is NOT a divergence: the spec
        // sanctions it twice over, at §6.5 ("an inadmissible .NET argument yields BL6017/BL6019")
        // and at §8.3 verbatim — "every type would fail on its inherited Equals(Object) since
        // Object is permanently Rejected". So this pins agreement with the spec, not a gap.
        var r = FrameworkOnly();

        Assert.That(Overload(r, "System.Text.RegularExpressions.Regex", NetCallForm.Instance,
                             "Equals", "System.Object").Outcome,
            Is.EqualTo(NetOverloadOutcome.NoMatch),
            "the winner must be a member of the §7.2 surface. Answering Resolved here would hand " +
            "Task 12 System.Object.Equals to emit an export for, undoing §7.2's exclusion of " +
            "System.Object's members — and §8.3 makes the Object parameter unmarshalable anyway.");

        Assert.That(Overload(r, "System.Text.StringBuilder", NetCallForm.Instance, "ToString")
                    .Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "the boundary is 'declared by System.Object', not 'named like an Object member': " +
            "StringBuilder OVERRIDES ToString, so it is a member of the surface and resolves.");
    }

    [Test]
    public void ResolveOverload_ANullaryObjectMemberOnANonOverridingTypeIsNoMatch_ARECORDEDSPECGAP()
    {
        // THE GENUINE DIVERGENCE, and it is deliberately not the Equals(Object) case above.
        // Stream does not override ToString or GetHashCode, so both resolve to System.Object's
        // declarations. They take ZERO arguments and return String/Int32 — no Object appears
        // anywhere in either signature, so neither §8.3's marshaling objection nor §6.5's
        // argument-side rule applies. `s.ToString()` compiles clean on the C# backend today, yet
        // §7.2 keeps System.Object's members out of the candidate set, so this is NoMatch — which
        // spec §6.3 turns into a HARD ERROR on the native backend in P2a-2.
        //
        // StringBuilder.ToString() cannot stand in for this: it OVERRIDES, so it is in the surface
        // and resolves. This test exists because that distinction leaves the real case unexercised.
        //
        // Pinning CURRENT behavior, not endorsing it. Escalated to the coordinator as a spec gap
        // (§14 shipped-divergence item + a §12.1 parity row). If §7.2 later admits the nullary
        // Object members, this test is the one to change, deliberately.
        var r = FrameworkOnly();

        Assert.That(Overload(r, "System.IO.Stream", NetCallForm.Instance, "ToString").Outcome,
            Is.EqualTo(NetOverloadOutcome.NoMatch),
            "pins current behavior: §7.2 excludes System.Object's members, so Stream.ToString() " +
            "does not resolve even though it is a valid call on the C# backend. If this starts " +
            "resolving, §7.2's surface changed — make sure that was intended and that Task 12 can " +
            "emit an export for it.");
        Assert.That(Overload(r, "System.IO.Stream", NetCallForm.Instance, "GetHashCode").Outcome,
            Is.EqualTo(NetOverloadOutcome.NoMatch),
            "same shape as ToString and for the same reason — nullary, no Object in the signature.");

        Assert.That(r.GetMembers("System.IO.Stream").Select(m => m.Name),
            Does.Not.Contain("GetHashCode"),
            "guard: the cause really is §7.2's exclusion — GetHashCode is absent from the surface, " +
            "so ResolveOverload is consistent with GetMembers here rather than losing a member.");
    }

    [Test]
    public void ResolveOverload_ValueTypeParameterlessConstructionHasNoMemberToResolve()
    {
        // Pins the consequence Task 4 predicted in GetMembers' remarks. Roslyn synthesizes a
        // public parameterless .ctor for every metadata value type; it is implicitly declared, so
        // it is not in the surface. `New Guid()` is default(Guid) — there is no metadata token to
        // call and therefore nothing for Task 12 to emit.
        Assert.That(Overload(FrameworkOnly(), "System.Guid", NetCallForm.Constructor, ".ctor")
                    .Outcome, Is.EqualTo(NetOverloadOutcome.NoMatch),
            "a zero-argument value-type construction is not a call. If this becomes Resolved, " +
            "NetTypeResolver started admitting implicitly-declared members and Task 12 will emit " +
            "a proxy slot for a constructor that has no metadata token.");
    }

    [Test]
    public void ResolveOverload_NarrowingDoesNotResolve()
    {
        Assert.That(Overload(FrameworkOnly(), "System.Math", NetCallForm.Static, "Abs",
                             "System.String").Outcome,
            Is.EqualTo(NetOverloadOutcome.NoMatch),
            "there is no implicit conversion from String to any Math.Abs parameter, so this is a " +
            "genuine BL6017. Resolving it would mean the matcher ignores parameter types.");
    }

    [Test]
    public void ResolveOverload_AnArgumentTypeThatDoesNotResolveIsNoMatch()
    {
        Assert.That(Overload(FrameworkOnly(), "System.Text.RegularExpressions.Regex",
                             NetCallForm.Instance, "IsMatch", "Contoso.NoSuchType").Outcome,
            Is.EqualTo(NetOverloadOutcome.NoMatch),
            "an argument whose type does not resolve cannot select an overload. DOCUMENTED " +
            "CONTRACT: there is no spelling for 'I do not know this argument's type' — a caller " +
            "that cannot type every argument must not ask, because a wildcard would either " +
            "fabricate a binding or turn a valid call into a spurious BL6018.");
    }

    [Test]
    public void ResolveOverload_MalformedSpellingsAreNoMatch_NotInjectedIntoTheProbe()
    {
        // Type and member names reach here from BasicLang source, and the implementation puts
        // them into synthesized C#. Every spelling is therefore validated as a single well-formed
        // type-name / identifier before it can be emitted.
        var r = FrameworkOnly();

        Assert.That(Overload(r, "System.Text.RegularExpressions.Regex", NetCallForm.Instance,
                             "IsMatch", "System.Int32) ; static void Boom(System.Int32 x").Outcome,
            Is.EqualTo(NetOverloadOutcome.NoMatch),
            "an argument spelling that is not exactly one type name must be rejected before it " +
            "reaches the synthesized probe source.");

        Assert.That(Overload(r, "System.Text.RegularExpressions.Regex", NetCallForm.Instance,
                             "IsMatch(\"\") ; //", "System.String").Outcome,
            Is.EqualTo(NetOverloadOutcome.NoMatch),
            "a member name that is not a C# identifier must be rejected the same way.");

        Assert.That(Overload(r, "System.Text.RegularExpressions.Regex", NetCallForm.Instance,
                             "IsMatch", (string)null!).Outcome,
            Is.EqualTo(NetOverloadOutcome.NoMatch),
            "a null argument spelling must answer NoMatch, never throw — this runs on the " +
            "analyzer and IntelliSense paths.");
    }

    [Test]
    public void ResolveOverload_ATypeLevelFailureIsNotAnOverloadAnswer()
    {
        // All three type-level failures must be distinguishable from "this type has no such
        // overload". A doc comment telling Task 8 to call ResolveTypeDetailed first is not
        // enforcement — if these collapsed into NoMatch, Task 8 would emit BL6017 "no matching
        // overload" for a type that was never found, and nothing would catch it.
        Assert.That(Overload(FrameworkOnly(), "System.Text.Rejex", NetCallForm.Instance, "IsMatch",
                             "System.String").Outcome,
            Is.EqualTo(NetOverloadOutcome.TypeUnavailable),
            "spec §11.4: a missing TYPE is BL6016, not BL6017. NoMatch here would report 'no " +
            "matching overload' for a type the user typo'd — the least actionable message " +
            "available. Fix NetTypeResolver.ResolveOverloadUncached, not the test.");

        const string source = "namespace Contoso { public class Dup2 { public void F(int i) { } } }";
        var a = EmitProbeAssembly("BlnetDupOverloadA", source);
        var b = EmitProbeAssembly("BlnetDupOverloadB", source);
        var r = NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { a, b }));

        Assert.That(Overload(r, "Contoso.Dup2", NetCallForm.Instance, "F", "System.Int32").Outcome,
            Is.EqualTo(NetOverloadOutcome.TypeUnavailable),
            "an AMBIGUOUS TYPE is BL6023 — spec §6.5 says so explicitly ('BL6018 covers ambiguous " +
            "overloads only'), so neither Ambiguous nor NoMatch may be returned here.");

        // Not effectively public: resolves at lookup, but no shim can reference it (csc: CS0122).
        var hidden = EmitProbeAssembly("BlnetHiddenOverload",
            "namespace Contoso { internal class Hush { public void F(int i) { } } }");
        var hr = NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { hidden }));

        Assert.That(Overload(hr, "Contoso.Hush", NetCallForm.Instance, "F", "System.Int32").Outcome,
            Is.EqualTo(NetOverloadOutcome.TypeUnavailable),
            "Lookup does not filter accessibility, so an internal type reaches the probe and draws " +
            "CS0122 — which would read as 'no such overload'. The truth is type-level: the type " +
            "cannot be named from generated code at all. Gate on IsEffectivelyPublic before " +
            "probing.");
        Assert.That(hr.ResolveType("Contoso.Hush")!.IsPublic, Is.False,
            "guard: the fixture's type really is non-public, so the assertion above is not vacuous");
    }

    [Test]
    public void ResolveOverload_TheResolvedMemberIsOneOfTheTypesSurfaceMembers()
    {
        // HONEST LABEL: this pins a CONSTRUCTION INVARIANT, not an independent oracle. It is true
        // by construction today — the only Resolved return hands back a descriptor drawn straight
        // from CandidateMembers, and GetMembers is CandidateMembers(...).Select(m => m.Descriptor),
        // so it cannot fail while its own guard passes. Do not read a pass here as evidence that
        // resolution and the surface were independently checked; that is the mistake Task 4's
        // tautological collision guard made, and the reason it stayed green while 186 members
        // vanished.
        //
        // It is kept as a TRIPWIRE: the moment anyone CONSTRUCTS a descriptor for the winner
        // instead of reusing the candidate's — which Task 12 makes likely, and which would
        // reintroduce System.Object's members into resolved results — this starts failing.
        var r = FrameworkOnly();
        var m = Overload(r, "System.Text.RegularExpressions.Regex", NetCallForm.Instance, "IsMatch",
                         "System.String");
        Assert.That(m.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved), "guard");

        Assert.That(r.GetMembers("System.Text.RegularExpressions.Regex").Select(x => x.ToString()),
            Does.Contain(m.Member!.ToString()),
            "the resolved member must be one GetMembers reports. If ResolveOverload can return a " +
            "member outside the surface, Task 12 emits no export for a call it resolved.");
    }

    [Test]
    public void ResolveOverload_AcceptsTheArgumentSpellingsTheResolverItselfProduces()
    {
        // Round-trip: NetMemberDescriptor's parameter spellings are the grammar ResolveOverload
        // accepts. Without this the two halves of the API speak different dialects and Task 8 has
        // to translate — which is where a `nint` vs System.IntPtr mismatch would come back.
        var r = FrameworkOnly();
        var declared = r.GetMembers("System.Text.RegularExpressions.Regex")
            .Single(x => x.Kind == NetMemberCategory.Method && x.Name == "IsMatch" && !x.IsStatic
                         && x.Parameters.Count == 2
                         && x.ParameterTypeFullNames.First() == "System.String");

        var m = r.ResolveOverload("System.Text.RegularExpressions.Regex", NetCallForm.Instance,
            declared.Name, declared.ParameterTypeFullNames.ToList());

        Assert.That(m.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "a parameter spelling GetMembers produced must be an acceptable ARGUMENT spelling. If " +
            "this fails the two sides of NetTypeResolver disagree on how a type is written.");
        Assert.That(m.Member!.ToString(), Is.EqualTo(declared.ToString()),
            "and it must resolve to the SAME member the spelling came from — a different overload " +
            "means the round-trip changed which type the spelling denotes.");
    }

    [Test]
    public void ResolveOverload_DistinctRequestsDoNotShareOneCacheEntry()
    {
        // The cache key joins the request's fields with control characters, which are INVISIBLE in
        // an editor -- the separator string literals look empty. If one is ever dropped, the key
        // degenerates to plain concatenation and these two requests become the same entry:
        //
        //   member "Abs"             + argument "System.Int32"  -> "...MathAbsSystem.Int32"
        //   member "AbsSystem.Int32" + no arguments             -> "...MathAbsSystem.Int32"
        //
        // whereupon whichever ran first would answer for both. The oracle is independent of the
        // key's construction: the two calls simply have different correct answers.
        var r = FrameworkOnly();

        var real = Overload(r, "System.Math", NetCallForm.Static, "Abs", "System.Int32");
        var spliced = Overload(r, "System.Math", NetCallForm.Static, "AbsSystem.Int32");

        Assert.That(real.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved), "Math.Abs(Integer) exists");
        Assert.That(spliced.Outcome, Is.EqualTo(NetOverloadOutcome.NoMatch),
            "there is no member named 'AbsSystem.Int32'. Resolved here means the cache key lost a " +
            "field separator and this request read the previous one's answer. The separators are " +
            "literal U+0001/U+0002/U+0003 — see the note in NetTypeResolver.ResolveOverload.");

        // ...and in the other order, against a fresh resolver, so neither ordering can hide it.
        var fresh = FrameworkOnly();
        Assert.That(Overload(fresh, "System.Math", NetCallForm.Static, "AbsSystem.Int32").Outcome,
            Is.EqualTo(NetOverloadOutcome.NoMatch),
            "guard for the reversed order: no member is named 'AbsSystem.Int32'");
        Assert.That(Overload(fresh, "System.Math", NetCallForm.Static, "Abs", "System.Int32").Outcome,
            Is.EqualTo(NetOverloadOutcome.Resolved),
            "order must not matter — a poisoned entry shows up in exactly one direction.");
    }

    [Test]
    public void ResolveOverload_AZeroArgumentCallDoesNotShareACacheEntryWithAnEmptySpelling()
    {
        // The collision the joined key had: string.Join(sep, new[]{ "" }) and
        // string.Join(sep, new string[0]) BOTH return "", so a zero-argument call and a call with
        // one EMPTY argument spelling computed the same key. The key is built before the spellings
        // are validated, so the cache was consulted and populated either way.
        //
        // The damaging direction is this one: cache the valid zero-argument answer first, then ask
        // with an argument the caller could not type. A shared entry returns Resolved WITH a Member
        // — fabricating a binding for an untypable argument, which is precisely what
        // ResolveOverload's contract says it exists to prevent. The fix is to PREFIX each list
        // element with its separator instead of joining between them, so [] and [""] differ.
        var r = FrameworkOnly();

        var nullary = Overload(r, "System.Text.StringBuilder", NetCallForm.Instance, "ToString");
        Assert.That(nullary.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "guard: StringBuilder.ToString() overrides, so it really is in the surface");

        var emptySpelling = Overload(r, "System.Text.StringBuilder", NetCallForm.Instance,
                                     "ToString", "");
        Assert.That(emptySpelling.Outcome, Is.EqualTo(NetOverloadOutcome.NoMatch),
            "an EMPTY argument spelling is not a well-formed type name, so this must be NoMatch. " +
            "Resolved here means it read the zero-argument call's cached answer — the key stopped " +
            "distinguishing an empty list from a list holding one empty string. Prefix each list " +
            "element with its separator in NetTypeResolver.ResolveOverload; do not relax this test.");
        Assert.That(emptySpelling.Member, Is.Null,
            "and it must carry NO member — handing Task 12 a member for an argument the caller " +
            "could not type is the fabricated binding this class exists to remove.");

        // Reversed order, fresh resolver: the collision poisons the VALID call the other way round.
        var fresh = FrameworkOnly();
        Assert.That(Overload(fresh, "System.Text.StringBuilder", NetCallForm.Instance, "ToString", "")
                    .Outcome, Is.EqualTo(NetOverloadOutcome.NoMatch),
            "guard for the reversed order");
        Assert.That(Overload(fresh, "System.Text.StringBuilder", NetCallForm.Instance, "ToString")
                    .Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "the valid zero-argument call must not inherit the invalid one's NoMatch — that would " +
            "be a spurious BL6017 on `sb.ToString()`.");

        // The same collision existed on the type-argument list, which is a separate field.
        Assert.That(r.ResolveOverload("System.Collections.Generic.Queue`1", NetCallForm.Instance,
                        "Enqueue", new[] { "System.Int32" }, new[] { "System.Int32" }).Outcome,
            Is.EqualTo(NetOverloadOutcome.Resolved), "guard");
        Assert.That(r.ResolveOverload("System.Collections.Generic.Queue`1", NetCallForm.Instance,
                        "Enqueue", new[] { "System.Int32" }, new[] { "" }).Outcome,
            Is.EqualTo(NetOverloadOutcome.NoMatch),
            "typeArguments is prefixed for the same reason arguments is — an empty type-argument " +
            "spelling must not collide with a populated one.");
    }

    [Test]
    public void ResolveOverload_IsCachedAndDoesNotAppendToDiagnostics()
    {
        // Diagnostics is fixed at construction (an ImmutableArray) precisely so a lookup path
        // cannot grow it. Overload resolution binds synthesized C# whose errors are the ANSWER,
        // not the resolver's diagnostics — leaking them would put CS1503 in a BasicLang build log.
        var r = FrameworkOnly();
        Assert.That(r.Diagnostics, Is.Empty, "guard: the framework closure is clean");

        for (var i = 0; i < 3; i++)
            Assert.That(Overload(r, "System.Text.RegularExpressions.Regex", NetCallForm.Constructor,
                                 ".ctor", "System.Int32").Outcome,
                Is.EqualTo(NetOverloadOutcome.NoMatch), "repeated lookups must be stable");

        Assert.That(r.Diagnostics, Is.Empty,
            "a failed overload lookup must not append to Diagnostics — that list is the BL6021 " +
            "reference-readability record and nothing else.");
    }

    [Test]
    public void AProjectWithNoReferencesStillResolvesFrameworkTypes()
    {
        // Spec §6.5 step 2: `Dim r As New Regex("a")` must resolve with no <Reference> element at
        // all. This is the resolver half of the invariant NetReferenceResolverTests pins on the
        // closure half.
        var closure = NetReferenceResolver.Resolve(
            new ProjectFile { Backend = "cpp" }, Path.Combine(_dir, "App.blproj"));
        Assert.That(closure.AssemblyPaths, Is.Empty, "guard: the project declares nothing");

        var resolver = NetTypeResolver.Create(closure.All);

        Assert.That(resolver.ResolveType("System.Text.RegularExpressions.Regex"), Is.Not.Null);
        Assert.That(resolver.ResolveType("System.Console"), Is.Not.Null);
        Assert.That(resolver.Diagnostics, Is.Empty,
            "the framework set must be clean: every entry readable as managed metadata. A " +
            "BL6021 here means the closure has started handing native DLLs to Roslyn.");
    }
}

/// <summary>
/// <c>TypeRegistry</c> is the LSP's .NET IntelliSense source, and until Task 7 it read assemblies
/// with <c>Assembly.LoadFrom</c> (<c>TypeRegistry.cs:145/:310/:346</c>) behind three bare
/// <c>catch {}</c> blocks. This fixture pins the two properties that made that a defect and the
/// observable shape that must survive the swap.
///
/// <para><b>Why the assertions go through <see cref="TypeRegistry"/> and not
/// <see cref="NetTypeResolver"/>.</b> The resolver already reads metadata without loading — a test
/// written against it is green before the fix and proves nothing. The LSP calls
/// <c>TypeRegistry</c>, so that is where the guard has to bite.</para>
///
/// <para><b>Why every probe assembly is COMPILED with a fresh identity rather than copied.</b>
/// Measured: <c>Assembly.LoadFrom</c> on a copy of an assembly already loaded in the process
/// (<c>BasicLang.dll</c>, <c>typeof(TypeRegistry).Assembly.Location</c>) resolves by ASSEMBLY
/// IDENTITY, hands back the instance already loaded, and never opens the copy at all — so the copy
/// is not locked and a delete-succeeds assertion passes with the defect fully in place. A
/// fresh-identity assembly is the only shape that locks: <c>LoadFrom</c> then throws
/// <c>UnauthorizedAccessException</c> on the delete. A guard that cannot fail is worse than no
/// guard, so do not "simplify" these back to <c>File.Copy</c>.</para>
/// </summary>
[TestFixture]
public class TypeRegistryMetadataTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "typereg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch { Thread.Sleep(200); }
        }
    }

    /// <summary>
    /// Compiles a real on-disk assembly whose simple name is unique to this test run, so that its
    /// presence in <c>AppDomain.CurrentDomain.GetAssemblies()</c> is unambiguous evidence of a load
    /// and <c>LoadFrom</c> cannot short-circuit to an already-loaded instance.
    /// </summary>
    private string EmitFreshAssembly(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            NetTypeResolverTestRefs.FrameworkPaths.Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(_dir, assemblyName + ".dll");
        var result = compilation.Emit(path);
        Assert.That(result.Success, Is.True,
            "the FIXTURE failed to build its probe assembly (not TypeRegistry): " + string.Join("\n",
                result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static string FreshName(string prefix) => prefix + Guid.NewGuid().ToString("N");

    /// <summary>
    /// A registry whose namespace-index cache lives in THIS test's temp directory.
    ///
    /// <para><b>Never call <c>new TypeRegistry()</c> here.</b> The parameterless constructor points at
    /// one user-scope file, <c>%LOCALAPPDATA%\BasicLang\namespace_index.json</c>, and
    /// <see cref="TypeRegistry.BuildIndex"/> CLEARS the index and then REPLACES that file. A fixture
    /// that builds an index over a temp directory therefore overwrites the developer's real LSP cache
    /// with entries naming assemblies that are deleted in TearDown — and because
    /// <c>LoadIndexFromCache</c> accepts any parseable file, the shipped language server then skips
    /// its own <c>BuildIndex</c> and runs on a one-namespace index of dead paths. That is exactly the
    /// empty-index failure this class was fixed to remove, reintroduced by the test suite. Measured:
    /// it happened.</para>
    /// </summary>
    private TypeRegistry NewRegistry() =>
        new TypeRegistry(Path.Combine(_dir, "namespace_index.json"));

    // ------------------------------------------------------------------
    // The two properties that made Assembly.LoadFrom a defect.
    // ------------------------------------------------------------------

    [Test]
    public void LoadTypesFromAssemblyDoesNotLockTheFile()
    {
        var name = FreshName("TypeRegLockProbe");
        var probe = EmitFreshAssembly(name, "namespace Contoso { public class Gadget { public int Spin() { return 1; } } }");

        var registry = NewRegistry();
        var types = registry.LoadTypesFromAssembly(probe);
        Assert.That(types.Select(t => t.FullName), Does.Contain("Contoso.Gadget"),
            "guard: the registry must actually have read this assembly, or the delete below is vacuous");

        Assert.That(() => File.Delete(probe), Throws.Nothing,
            "Assembly.LoadFrom holds a lock here — measured, it throws UnauthorizedAccessException " +
            "on this delete. Spec §6.2 replaces it precisely because the LSP could not reload a " +
            "rebuilt referenced assembly without restarting. Fix TypeRegistry, not this test.");
    }

    [Test]
    public void LoadTypesFromAssemblyDoesNotLoadThatAssemblyIntoTheProcess()
    {
        var name = FreshName("TypeRegLoadProbe");
        var probe = EmitFreshAssembly(name, "namespace Contoso { public class Gizmo { } }");

        var registry = NewRegistry();
        Assert.That(registry.LoadTypesFromAssembly(probe).Select(t => t.FullName),
            Does.Contain("Contoso.Gizmo"), "guard: the registry must actually have read the probe");

        Assert.That(AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name),
            Does.Not.Contain(name),
            "TypeRegistry must READ metadata, never LOAD it. A load runs module initializers and " +
            "cannot be undone, and it is why the index silently degraded. Fix TypeRegistry.");
    }

    [Test]
    public void BuildIndexDoesNotLockTheAssembliesItScans()
    {
        var name = FreshName("TypeRegScanProbe");
        EmitFreshAssembly(name, "namespace Contoso.Scanned { public class Seen { } }");

        var registry = NewRegistry();
        registry.AddSearchPath(_dir);
        registry.BuildIndex();

        Assert.That(registry.LoadNamespace("Contoso.Scanned"), Is.True,
            "guard: BuildIndex must have indexed the probe's namespace, or the delete is vacuous");
        Assert.That(() => File.Delete(Path.Combine(_dir, name + ".dll")), Throws.Nothing,
            "ScanAssembly (TypeRegistry.cs:145) used Assembly.LoadFrom, which locks every DLL in " +
            "every search path for the life of the LSP process. Fix TypeRegistry.");
    }

    /// <summary>
    /// The measured production failure: the LSP runs on net8.0 while
    /// <see cref="TypeRegistry.DetectDotNetSdkPath"/> hands it the NEWEST installed reference pack.
    /// <c>Assembly.LoadFrom</c> cannot load a reference assembly built for a higher framework
    /// version than the running runtime — measured 141 of 164 failures over
    /// <c>Microsoft.NETCore.App.Ref/9.0.12</c> on a net8.0 host — and the bare <c>catch {}</c> made
    /// that look like success, so the whole namespace index was empty and nobody noticed. Reading
    /// metadata has no such version coupling.
    /// </summary>
    [Test]
    public void IndexesAnAssemblyBuiltForANewerFrameworkThanTheRunningRuntime()
    {
        var newerRefPack = Directory
            .GetDirectories(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet", "packs", "Microsoft.NETCore.App.Ref"))
            .Select(d => new { Dir = d, Name = Path.GetFileName(d) })
            .Where(d => Version.TryParse(d.Name, out var v) && v.Major > Environment.Version.Major)
            .OrderBy(d => d.Name)
            .FirstOrDefault();

        if (newerRefPack == null)
            Assert.Ignore("no reference pack newer than the running runtime is installed; " +
                          "this machine cannot exhibit the version-coupling failure.");

        var refDir = Directory.GetDirectories(Path.Combine(newerRefPack!.Dir, "ref")).OrderBy(d => d).First();
        var systemRuntime = Path.Combine(refDir, "System.Runtime.dll");
        Assert.That(File.Exists(systemRuntime), Is.True, "guard: the ref pack must contain System.Runtime.dll");

        var registry = NewRegistry();
        var types = registry.LoadTypesFromAssembly(systemRuntime);

        Assert.That(types, Is.Not.Empty,
            $"reading {Path.GetFileName(refDir)} reference metadata on a net{Environment.Version.Major} " +
            "host must work. Assembly.LoadFrom fails here with FileLoadException ('Could not load " +
            "file or assembly ... Version=" + newerRefPack.Name + "') and the bare catch {} hid it, " +
            "leaving the LSP's namespace index permanently empty. Fix TypeRegistry.");
    }

    // ------------------------------------------------------------------
    // Failures are reported, not swallowed.
    // ------------------------------------------------------------------

    [Test]
    public void AnUnreadableAssemblyIsReportedAsBL6021_NotSwallowed()
    {
        var junk = Path.Combine(_dir, "Junk.dll");
        File.WriteAllBytes(junk, new byte[] { 0x4D, 0x5A });        // "MZ" and nothing else

        var registry = NewRegistry();
        List<NetTypeInfo> types = null!;
        Assert.That(() => types = registry.LoadTypesFromAssembly(junk), Throws.Nothing,
            "every path here is user-reachable — a <Reference> can name a native DLL or a truncated " +
            "file — so an unreadable assembly must never crash the LSP request.");
        Assert.That(types, Is.Empty);

        Assert.That(registry.Diagnostics.Any(d => d.Contains("BL6021")), Is.True,
            "the three call sites used a bare catch {}, which is why nobody noticed the index was " +
            "empty. An unreadable reference must leave a BL6021 behind. Fix TypeRegistry.");
        Assert.That(registry.Diagnostics.Any(d => d.Contains("Junk.dll")), Is.True,
            "a diagnostic that does not name the file it is about cannot be acted on.");
    }

    [Test]
    public void AMissingAssemblyIsReportedAsBL6021_NotSwallowed()
    {
        var missing = Path.Combine(_dir, "NeverExisted.dll");

        var registry = NewRegistry();
        Assert.That(() => registry.LoadTypesFromAssembly(missing), Throws.Nothing);
        Assert.That(registry.Diagnostics.Any(d => d.Contains("BL6021") && d.Contains("NeverExisted.dll")),
            Is.True,
            "a <Reference> deleted between resolution and use must be reported, not swallowed. " +
            "Fix TypeRegistry.");
    }

    // ------------------------------------------------------------------
    // Observable shape: what the LSP's completion lists are built from.
    // ------------------------------------------------------------------

    [Test]
    public void ReadsTheShapeTheLspConsumesFromAFreshAssembly()
    {
        var name = FreshName("TypeRegShapeProbe");
        var probe = EmitFreshAssembly(name, @"
namespace Contoso
{
    public class Widget : Base, System.IDisposable
    {
        public Widget() { }
        public Widget(int spokes) { }
        public int Spokes { get; set; }
        public string Label;
        public static string Describe(int n, string tag = ""x"") { return tag; }
        public void Dispose() { }
    }
    public class Base { public void Inherited() { } }
    public enum Gear { Low, High }
    public static class Helpers { public static int Twice(int n) { return n * 2; } }
}");

        var registry = NewRegistry();
        registry.LoadTypesFromAssembly(probe);

        var widget = registry.GetType("Contoso.Widget");
        Assert.That(widget, Is.Not.Null, "the LSP looks types up by full name");
        Assert.That(registry.GetType("Widget"), Is.Not.Null, "…and by simple name");

        Assert.That(widget!.Name, Is.EqualTo("Widget"));
        Assert.That(widget.Namespace, Is.EqualTo("Contoso"));
        Assert.That(widget.IsClass, Is.True);
        Assert.That(widget.IsInterface, Is.False);
        Assert.That(widget.IsEnum, Is.False);
        Assert.That(widget.IsStruct, Is.False);
        Assert.That(widget.IsStatic, Is.False);
        Assert.That(widget.BaseType, Is.EqualTo("Base"));
        Assert.That(widget.Interfaces, Does.Contain("IDisposable"));

        Assert.That(widget.Constructors.Count, Is.EqualTo(2), "both declared public constructors");
        Assert.That(widget.Constructors.Select(c => c.Name), Is.All.EqualTo("New"),
            "the LSP renders constructors as VB's New");
        Assert.That(widget.Constructors.All(c => c.Kind == NetMemberKind.Constructor), Is.True);

        var members = widget.Members;
        var spokes = members.Single(m => m.Name == "Spokes");
        Assert.That(spokes.Kind, Is.EqualTo(NetMemberKind.Property));
        Assert.That(spokes.ReturnType, Is.EqualTo("Integer"), "VB spellings, not C# ones");
        Assert.That(spokes.CanRead, Is.True);
        Assert.That(spokes.CanWrite, Is.True);

        var label = members.Single(m => m.Name == "Label");
        Assert.That(label.Kind, Is.EqualTo(NetMemberKind.Field));
        Assert.That(label.ReturnType, Is.EqualTo("String"));

        var describe = members.Single(m => m.Name == "Describe");
        Assert.That(describe.Kind, Is.EqualTo(NetMemberKind.Method));
        Assert.That(describe.IsStatic, Is.True);
        Assert.That(describe.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "n", "tag" }));
        Assert.That(describe.Parameters.Select(p => p.Type), Is.EqualTo(new[] { "Integer", "String" }));
        Assert.That(describe.Parameters[1].IsOptional, Is.True,
            "signature help renders optional parameters differently");

        Assert.That(members.Select(m => m.Name), Does.Contain("Inherited"),
            "reflection's GetMethods(Public|Instance|Static) is TRANSITIVE, so the LSP has always " +
            "offered inherited members. A metadata walk that stops at the queried type silently " +
            "shortens every completion list.");
        Assert.That(members.Select(m => m.Name), Does.Contain("GetHashCode"),
            "System.Object's members too — NetTypeResolver.CandidateMembers deliberately EXCLUDES " +
            "them for §7.2's shim surface, which is the wrong rule for a completion list. These " +
            "two member sets are not interchangeable.");

        Assert.That(members.Select(m => m.Name), Does.Not.Contain("get_Spokes"),
            "reflection skipped IsSpecialName accessors; a property is one member.");

        Assert.That(registry.GetType("Contoso.Helpers")!.IsStatic, Is.True,
            "reflection reported IsAbstract && IsSealed as IsStatic");

        var gear = registry.GetType("Contoso.Gear");
        Assert.That(gear!.IsEnum, Is.True);
        Assert.That(gear.Members.Where(m => m.Kind == NetMemberKind.EnumValue).Select(m => m.Name),
            Is.EquivalentTo(new[] { "Low", "High" }));
    }

    /// <summary>
    /// The one member reflection reported that metadata cannot, decided deliberately rather than
    /// discovered later. Reflection's <c>GetFields(Public|Instance|Static)</c> surfaces an enum's
    /// compiler-generated <c>value__</c> backing field; Roslyn's PE symbols never expose it. It is
    /// not a member a user can write, so a completion list offering <c>DayOfWeek.value__</c> was a
    /// defect. Dropping it is the intended difference.
    /// </summary>
    [Test]
    public void AnEnumsValueBackingFieldIsNotOfferedAsAMember()
    {
        var name = FreshName("TypeRegEnumProbe");
        var probe = EmitFreshAssembly(name, "namespace Contoso { public enum Gear { Low, High } }");

        var registry = NewRegistry();
        registry.LoadTypesFromAssembly(probe);

        var members = registry.GetType("Contoso.Gear")!.Members.Select(m => m.Name).ToList();

        // Does.Not.Contain passes on an empty list, so the positive guard is what stops this test
        // from certifying a registry that produced no members at all.
        Assert.That(members, Does.Contain("Low"), "guard: the enum's members must have been read");
        Assert.That(members, Does.Contain("High"));
        Assert.That(members, Does.Not.Contain("value__"),
            "value__ is a compiler-generated backing field, not something a user can write. If " +
            "this starts failing, TypeRegistry has gone back to reflection over a loaded assembly.");
    }

    /// <summary>
    /// <c>PreloadCoreTypes</c> still describes the RUNNING runtime through reflection — it has no
    /// <c>Assembly.LoadFrom</c> and Task 7 does not touch it. So the file now has TWO producers of
    /// <see cref="NetTypeInfo"/>, and a completion list must not change shape depending on which
    /// one answered.
    /// </summary>
    [Test]
    public void TheReflectionAndMetadataProducersAgreeOnShape()
    {
        // Regex and NOT StringBuilder: StringBuilder is declared by System.Private.CoreLib, which
        // Roslyn cannot read as a metadata reference at all, and the System.Runtime.dll that appears
        // to export it is a pure type-forwarder that declares nothing. Regex is declared by an
        // ordinary readable framework assembly and is in PreloadCoreTypes' curated list.
        const string name = "System.Text.RegularExpressions.Regex";

        var registry = NewRegistry();
        registry.PreloadCoreTypes();
        var preloaded = registry.GetType(name);
        Assert.That(preloaded, Is.Not.Null, "guard: PreloadCoreTypes must register Regex");

        var fromMetadata = NewRegistry();
        fromMetadata.LoadTypesFromAssembly(typeof(System.Text.RegularExpressions.Regex).Assembly.Location);
        var metadata = fromMetadata.GetType(name);
        Assert.That(metadata, Is.Not.Null,
            "guard: the metadata producer must have read Regex out of " +
            Path.GetFileName(typeof(System.Text.RegularExpressions.Regex).Assembly.Location));

        Assert.That(metadata!.Name, Is.EqualTo(preloaded!.Name));
        Assert.That(metadata.FullName, Is.EqualTo(preloaded.FullName));
        Assert.That(metadata.Namespace, Is.EqualTo(preloaded.Namespace));
        Assert.That(metadata.IsClass, Is.EqualTo(preloaded.IsClass));
        Assert.That(metadata.IsStruct, Is.EqualTo(preloaded.IsStruct));
        Assert.That(metadata.IsEnum, Is.EqualTo(preloaded.IsEnum));
        Assert.That(metadata.IsStatic, Is.EqualTo(preloaded.IsStatic));
        Assert.That(metadata.IsGeneric, Is.EqualTo(preloaded.IsGeneric));
        Assert.That(metadata.BaseType, Is.EqualTo(preloaded.BaseType));
        Assert.That(metadata.GenericParameters, Is.EqualTo(preloaded.GenericParameters));
        Assert.That(metadata.Interfaces, Is.EquivalentTo(preloaded.Interfaces));

        // Guard first: an empty list on both sides would make every set comparison below vacuous.
        Assert.That(metadata.Members.Select(m => m.Name), Does.Contain("IsMatch"));
        Assert.That(metadata.Members.Select(m => m.Name), Does.Contain("Options"),
            "a property the reflection producer reported too");
        Assert.That(metadata.Constructors, Is.Not.Empty);

        // FULL SIGNATURE comparison, not a handful of scalars. Comparing only names and flags is how
        // a spelling divergence in PARAMETER types slipped through review once already: the
        // reflection ladder never matched its primitive arms for a by-ref parameter and fell through
        // to Type.Name, so Int32.TryParse read "Int32&" from one producer and "Integer&" from the
        // other. Both producers are live in one registry, so that put two spellings of the same
        // signature into two completion lists.
        Assert.That(metadata.Members.Select(m => m.GetSignature()).OrderBy(s => s, StringComparer.Ordinal),
            Is.EqualTo(preloaded.Members.Select(m => m.GetSignature()).OrderBy(s => s, StringComparer.Ordinal)),
            "the reflection and metadata producers must describe the same type identically, down to " +
            "every parameter's spelling — they both feed the same completion lists. Fix whichever " +
            "producer is wrong in TypeRegistry, not this test.");
        Assert.That(metadata.Constructors.Select(m => m.GetSignature()).OrderBy(s => s, StringComparer.Ordinal),
            Is.EqualTo(preloaded.Constructors.Select(m => m.GetSignature()).OrderBy(s => s, StringComparer.Ordinal)));
    }

    /// <summary>
    /// The by-ref spelling both producers must agree on, pinned directly rather than only through
    /// the whole-type comparison above — this is the exact shape that diverged.
    /// </summary>
    [Test]
    public void ByRefParametersUseTheVbSpellingInBothProducers()
    {
        var reflection = NewRegistry();
        reflection.PreloadCoreTypes();
        var tryParse = reflection.GetType("System.Int32")!.Members
            .First(m => m.Name == "TryParse" && m.Parameters.Count == 2);

        Assert.That(tryParse.Parameters[1].Type, Is.EqualTo("Integer&"),
            "the by-ref suffix goes on the VB name. typeof(int).MakeByRefType() is not typeof(int), " +
            "so the reflection ladder's primitive arms never matched and it used to report the CLR " +
            "spelling 'Int32&' — while the metadata producer reported 'Integer&'. Every other name " +
            "this class produces is VB, so VB wins.");

        var name = FreshName("TypeRegByRefProbe");
        var probe = EmitFreshAssembly(name,
            "namespace Contoso { public static class Parsers { " +
            "public static bool Try(string s, out int value) { value = 0; return true; } } }");
        var metadata = NewRegistry();
        metadata.LoadTypesFromAssembly(probe);

        Assert.That(metadata.GetType("Contoso.Parsers")!.Members.First(m => m.Name == "Try")
                .Parameters[1].Type,
            Is.EqualTo("Integer&"),
            "…and the metadata producer must spell it the same way.");
    }

    // ------------------------------------------------------------------
    // Reference-set composition. Roslyn resolves an assembly's references only against what it was
    // given, so WHICH framework is in the set decides whether a base type resolves at all.
    // ------------------------------------------------------------------

    /// <summary>
    /// A lone assembly — a project <c>&lt;Reference&gt;</c>, the common case — needs the shared
    /// framework added, or its base types bind to error symbols.
    /// </summary>
    [Test]
    public void AnAssemblyReadOnItsOwnStillResolvesItsBaseTypes()
    {
        var name = FreshName("TypeRegSoloProbe");
        var probe = EmitFreshAssembly(name,
            "namespace Contoso { public class Solo { } public enum Lonely { One } }");

        var registry = NewRegistry();
        registry.LoadTypesFromAssembly(probe);

        Assert.That(registry.GetType("Contoso.Solo")!.BaseType, Is.EqualTo("Object"),
            "without the shared framework in the reference set a base type binds to an ERROR " +
            "symbol. Reflection never hit this because a LOADED assembly resolves its references " +
            "through the runtime.");
        Assert.That(registry.GetType("Contoso.Solo")!.Members.Select(m => m.Name),
            Does.Contain("GetHashCode"),
            "…and System.Object contributing no members silently shortens every completion list.");
        Assert.That(registry.GetType("Contoso.Lonely")!.IsEnum, Is.True,
            "a type's enum-ness IS 'its base type is System.Enum', so an unresolvable corlib makes " +
            "a public enum report IsEnum = false.");
    }

    /// <summary>
    /// The LSP's own configuration: the search path is a reference PACK, which brings its own core
    /// library. Adding the shared framework on top would put TWO framework versions in one
    /// compilation — measured, that is strictly worse than none, because every core type is declared
    /// twice and <c>System.Enum</c> then resolves to nothing at all.
    /// </summary>
    [Test]
    public void AReferencePackIsReadWithoutMixingInASecondFrameworkVersion()
    {
        var sdkPath = TypeRegistry.DetectDotNetSdkPath();
        if (string.IsNullOrEmpty(sdkPath))
            Assert.Ignore("no Microsoft.NETCore.App.Ref pack installed.");

        var registry = NewRegistry();
        registry.AddSearchPath(sdkPath!);
        registry.BuildIndex();

        Assert.That(registry.LoadNamespace("System.IO"), Is.True, "guard: the index must be populated");

        var fileMode = registry.GetType("System.IO.FileMode");
        Assert.That(fileMode, Is.Not.Null, "guard: FileMode must be readable from the pack");
        Assert.That(fileMode!.IsEnum, Is.True,
            "FileMode reporting IsEnum = false is the signature of two framework versions in one " +
            "compilation: System.Enum is declared twice, resolves to neither, and the enum stops " +
            "looking like an enum. The reference pack brings its own core library and must own the " +
            "framework alone — see TypeRegistry.EffectiveReferenceSet.");
        Assert.That(fileMode.Members.Where(m => m.Kind == NetMemberKind.EnumValue).Select(m => m.Name),
            Does.Contain("Open"));

        Assert.That(registry.Diagnostics, Is.Empty,
            "a healthy reference pack must produce no BL6021. A crop of 'not a managed assembly' " +
            "warnings here means the reference set is self-conflicting, not that the pack is bad.");
    }

    /// <summary>
    /// Nested public types were reachable through reflection's <c>GetExportedTypes</c>, which
    /// flattens them, spells their <c>FullName</c> with <c>+</c> and reports the ENCLOSING
    /// namespace. A metadata walk that only visits namespace members drops them entirely, and
    /// <c>System.Environment.SpecialFolder</c> is exactly the kind of name a user completes.
    /// </summary>
    [Test]
    public void NestedPublicTypesAreIndexedTheWayReflectionReportedThem()
    {
        var name = FreshName("TypeRegNestProbe");
        var probe = EmitFreshAssembly(name,
            "namespace Contoso { public class Outer { public enum Inner { A, B } } }");

        var registry = NewRegistry();
        registry.LoadTypesFromAssembly(probe);

        var inner = registry.GetType("Contoso.Outer+Inner");
        Assert.That(inner, Is.Not.Null,
            "reflection's Type.FullName spells nesting with '+', and the registry indexed that " +
            "spelling. Dropping it changes what the LSP can look up.");
        Assert.That(inner!.Name, Is.EqualTo("Inner"));
        Assert.That(inner.Namespace, Is.EqualTo("Contoso"),
            "reflection reports the ENCLOSING namespace for a nested type, not 'Contoso.Outer'");
        Assert.That(inner.IsEnum, Is.True);
        Assert.That(registry.GetType("Inner"), Is.Not.Null, "…and by simple name");
    }

    /// <summary>
    /// Generic types keep reflection's metadata spelling: <c>List`1</c>, arity-suffixed, indexed
    /// additionally under the bare name so <c>List</c> resolves.
    /// </summary>
    [Test]
    public void GenericTypesKeepTheirMetadataArityAndVbDisplayName()
    {
        var name = FreshName("TypeRegGenProbe");
        var probe = EmitFreshAssembly(name,
            "namespace Contoso { public class Box<T> { public T Item; } }");

        var registry = NewRegistry();
        registry.LoadTypesFromAssembly(probe);

        var box = registry.GetType("Contoso.Box`1");
        Assert.That(box, Is.Not.Null, "reflection's Type.Name for a generic carries the arity suffix");
        Assert.That(box!.IsGeneric, Is.True);
        Assert.That(box.GenericParameters, Is.EqualTo(new[] { "T" }));
        Assert.That(box.DisplayName, Is.EqualTo("Box(Of T)"),
            "DisplayName is what completion renders; it strips the arity and uses VB syntax");
    }

    // ------------------------------------------------------------------
    // Resolver ownership (spec §6.1 measured 209 ms cold / 46-49 ms per fresh instance).
    // ------------------------------------------------------------------

    [Test]
    public void TheResolverIsBuiltOncePerRegistryAndReusedAcrossCalls()
    {
        var a = FreshName("TypeRegOwnA");
        var b = FreshName("TypeRegOwnB");
        EmitFreshAssembly(a, "namespace Contoso { public class Alpha { } }");
        EmitFreshAssembly(b, "namespace Contoso { public class Beta { } }");

        var registry = NewRegistry();
        registry.AddSearchPath(_dir);
        registry.BuildIndex();
        var afterIndex = registry.ResolverBuildCount;

        // The guard has to be that the index actually WORKED, not merely that a resolver was built.
        // ResolverFor runs before any assembly is read, so `afterIndex > 0` is satisfied by a .dll
        // merely existing in the search path — and if the metadata walk returned nothing,
        // LoadNamespace would return false without ever calling ResolverFor again and the equality
        // below would hold trivially. That version of this test passes against a broken registry.
        Assert.That(registry.LoadNamespace("Contoso"), Is.True,
            "guard: BuildIndex must have indexed the probes' namespace, or the count below is vacuous");
        Assert.That(registry.GetType("Contoso.Alpha"), Is.Not.Null, "guard: and actually read them");
        Assert.That(registry.GetType("Contoso.Beta"), Is.Not.Null);
        Assert.That(afterIndex, Is.GreaterThan(0), "guard: BuildIndex must have built one");

        registry.LoadNamespace("Contoso");
        registry.GetType("Contoso.Alpha");

        Assert.That(registry.ResolverBuildCount, Is.EqualTo(afterIndex),
            "construction costs 209 ms cold and 46-49 ms per fresh instance over a framework " +
            "closure, and a fresh instance throws away the lookup cache. One per registry, reused. " +
            "If this fails, TypeRegistry is rebuilding per call.");
    }

    [Test]
    public void ANewAssemblyPathRebuildsTheResolverExactlyOnce()
    {
        var a = FreshName("TypeRegAddA");
        EmitFreshAssembly(a, "namespace Contoso { public class Alpha { } }");
        var registry = NewRegistry();
        registry.AddSearchPath(_dir);
        registry.BuildIndex();
        var afterIndex = registry.ResolverBuildCount;

        var outside = Path.Combine(_dir, "outside");
        Directory.CreateDirectory(outside);
        var b = FreshName("TypeRegAddB");
        var compilation = CSharpCompilation.Create(b,
            new[] { CSharpSyntaxTree.ParseText("namespace Contoso { public class Beta { } }") },
            NetTypeResolverTestRefs.FrameworkPaths.Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var bPath = Path.Combine(outside, b + ".dll");
        Assert.That(compilation.Emit(bPath).Success, Is.True, "fixture guard");

        Assert.That(registry.LoadTypesFromAssembly(bPath).Select(t => t.FullName),
            Does.Contain("Contoso.Beta"), "a path the resolver has never seen must still resolve");
        var afterAdd = registry.ResolverBuildCount;
        Assert.That(afterAdd, Is.EqualTo(afterIndex + 1),
            "a genuinely new assembly path is the ONE thing that justifies a rebuild");

        registry.LoadTypesFromAssembly(bPath);
        Assert.That(registry.ResolverBuildCount, Is.EqualTo(afterAdd),
            "…and the rebuilt resolver must then be reused, not rebuilt per call");
    }

    // ------------------------------------------------------------------
    // The two-enum hazard NetTypeDescriptors.cs warns about.
    // ------------------------------------------------------------------

    /// <summary>
    /// <c>BasicLang.Compiler.SemanticAnalysis.NetMemberKind</c> is <c>Method=0 … Constructor=5</c>;
    /// <c>BasicLang.Net.NetMemberCategory</c> is <c>Constructor=0 … Field=3</c>. Task 7 maps Roslyn
    /// symbols STRAIGHT onto the former and never converts between the two, because every value
    /// they share disagrees. This pins the disagreement so that a future int cast or "unification"
    /// cannot look harmless.
    /// </summary>
    [Test]
    public void TheTwoMemberKindEnumsAreNotIntCompatible()
    {
        Assert.That((int)NetMemberKind.Method, Is.EqualTo(0));
        Assert.That((int)NetMemberKind.Property, Is.EqualTo(1));
        Assert.That((int)NetMemberKind.Field, Is.EqualTo(2));
        Assert.That((int)NetMemberKind.Event, Is.EqualTo(3));
        Assert.That((int)NetMemberKind.EnumValue, Is.EqualTo(4));
        Assert.That((int)NetMemberKind.Constructor, Is.EqualTo(5));

        Assert.That((int)NetMemberCategory.Constructor, Is.EqualTo(0));
        Assert.That((int)NetMemberCategory.Method, Is.EqualTo(1));
        Assert.That((int)NetMemberCategory.Property, Is.EqualTo(2));
        Assert.That((int)NetMemberCategory.Field, Is.EqualTo(3));

        Assert.That((int)NetMemberCategory.Constructor, Is.Not.EqualTo((int)NetMemberKind.Constructor),
            "these two enums must never be round-tripped through an int. If you are here because " +
            "you want one enum, RENAME rather than renumber — NetMemberKind is public API consumed " +
            "by SemanticAnalyzer, SymbolTable, ExternalLibraryLoader and four LSP files.");
        Assert.That((int)NetMemberCategory.Method, Is.Not.EqualTo((int)NetMemberKind.Method));
        Assert.That((int)NetMemberCategory.Property, Is.Not.EqualTo((int)NetMemberKind.Property));
        Assert.That((int)NetMemberCategory.Field, Is.Not.EqualTo((int)NetMemberKind.Field));
    }

    // ------------------------------------------------------------------
    // Cache bounds (constraint (a) measured during Tasks 4-5).
    // ------------------------------------------------------------------

    /// <summary>
    /// The overload cache is keyed on a whole REQUEST — call form, type, member, type-args and each
    /// argument spelling — so on the IntelliSense path typing <c>Regex.IsMatch(</c> one character at
    /// a time buys a distinct entry per keystroke, entries are created BEFORE the spellings are
    /// validated (so rejecting a malformed spelling still costs a slot), and each holds a
    /// <see cref="NetMemberDescriptor"/> with its own parameter list. Entry count is combinatorial
    /// in half-typed identifiers, not linear in names, and the resolver's lifetime is the closure's.
    ///
    /// <para>Reordering the key so invalid input is validated first was rejected: it moves that
    /// input from cached to recomputed at ~2 ms per probe. The cache is BOUNDED instead.</para>
    /// </summary>
    [Test]
    public void TheOverloadCacheIsBounded()
    {
        var resolver = NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths);
        var cap = NetTypeResolver.MaxOverloadCacheEntries;

        // "Nope 5" — with the space — is NOT one well-formed type name, so each of these is
        // rejected by the spelling gate WITHOUT paying the ~2 ms probe, and still keys an entry.
        // That is precisely the growth path the cap exists for (entries are created before
        // validation), and it also keeps this test off a cap-times-2 ms treadmill.
        for (var i = 0; i < cap + 50; i++)
        {
            resolver.ResolveOverload("System.Text.RegularExpressions.Regex", NetCallForm.Static,
                "IsMatch", new[] { "System.String", "Nope " + i });
        }

        Assert.That(resolver.OverloadCacheCount, Is.LessThanOrEqualTo(cap),
            "an unbounded request-keyed cache on the IntelliSense path grows per keystroke and is " +
            "never released while the closure lives. Bound it in NetOverloadProbe.cs.");

        // Regex.IsMatch(String) is an INSTANCE member; the Shared overloads take two strings.
        Assert.That(resolver.ResolveOverload("System.Text.RegularExpressions.Regex",
                NetCallForm.Static, "IsMatch", new[] { "System.String", "System.String" }).Outcome,
            Is.EqualTo(NetOverloadOutcome.Resolved),
            "bounding must not break correctness — a real call still resolves after eviction");
    }

    [Test]
    public void TheTypeLookupCacheIsBounded()
    {
        var resolver = NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths);
        var cap = NetTypeResolver.MaxTypeCacheEntries;

        for (var i = 0; i < cap + 50; i++)
            resolver.ResolveType("Contoso.NeverExists" + i);

        Assert.That(resolver.TypeCacheCount, Is.LessThanOrEqualTo(cap),
            "keyed on arbitrary user text, so every half-typed identifier is a permanent entry. " +
            "Entries are far smaller than the overload cache's and the miss path is dearer to " +
            "recompute, which is why the cap is higher — but unbounded is still unbounded.");
        Assert.That(resolver.ResolveType("System.Console"), Is.Not.Null,
            "bounding must not break correctness");
    }
}
