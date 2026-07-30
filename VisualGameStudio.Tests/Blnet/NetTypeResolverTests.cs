using BasicLang.Net;
using BasicLang.Compiler.ProjectSystem;   // NOT BasicLang.ProjectSystem — see ProjectFile.cs:8
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
