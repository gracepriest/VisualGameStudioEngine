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
        Assert.That(t.Kind, Is.EqualTo(NetTypeKind.Class));
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
            "namespace Contoso { public class Base { public void FromBase() { } } " +
            "public class Derived : Base { public void FromDerived() { } } }");
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
    }

    [Test]
    public void MemberCarriesTheThreeInputsTheManglerNeeds()
    {
        // §7.3: NetNameMangler produces its identifier from (declaring type, member name,
        // parameter types). All three must be readable off a NetMemberInfo.
        var overloads = FrameworkOnly().GetMembers("System.Text.RegularExpressions.Regex")
            .Where(m => m.Name == "IsMatch" && m.Kind == NetMemberKind.Method)
            .ToList();

        var instanceOneArg = overloads.Single(m =>
            !m.IsStatic && m.ParameterTypeFullNames.Count == 1 &&
            m.ParameterTypeFullNames[0] == "System.String");

        Assert.That(instanceOneArg.DeclaringTypeFullName,
            Is.EqualTo("System.Text.RegularExpressions.Regex"));
        Assert.That(instanceOneArg.TypeFullName, Is.EqualTo("System.Boolean"),
            "parameter and return type names must be fully qualified and NOT C# keywords — " +
            "'bool' and 'System.Boolean' would mangle to different identifiers for one method.");
        Assert.That(overloads.Any(m => m.IsStatic), Is.True,
            "the static overload set must be present too; Regex.IsMatch(String, String) is static.");
        Assert.That(
            overloads.Select(m => string.Join(",", m.ParameterTypeFullNames)).Distinct().Count(),
            Is.EqualTo(overloads.Count),
            "no two overloads may present identical parameter-type lists, or §7.3's " +
            "'two overloads never collide' requirement is unsatisfiable however the mangler is " +
            "written. Fix the parameter-type formatting in NetTypeResolver.");
    }

    [Test]
    public void ConstructorsAreEnumeratedUnderTheirMetadataName()
    {
        var ctors = FrameworkOnly().GetMembers("System.Text.RegularExpressions.Regex")
            .Where(m => m.Kind == NetMemberKind.Constructor).ToList();

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
        Assert.That(members.Single(m => m.Name == "Length").Kind, Is.EqualTo(NetMemberKind.Property));
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
