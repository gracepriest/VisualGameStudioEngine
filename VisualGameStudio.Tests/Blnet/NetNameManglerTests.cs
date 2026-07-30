using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Spec §7.3's <c>NetNameMangler</c>: the export name that appears on both sides of the P2a-1
/// ABI (Task 12's <c>BlnetProxyTable</c> slot, Task 14's shim export). A collision here means two
/// distinct .NET members share one export slot and a call silently reaches the wrong member — so
/// every "distinctness" assertion in this file is checked against an oracle built directly from
/// <see cref="NetMemberDescriptor"/>'s fields by code that never calls into
/// <see cref="NetNameMangler"/> itself (<see cref="IndependentSignatureKey"/>). A test that instead
/// grouped by the mangler's own output — or by whatever key its implementation happens to
/// deduplicate on — would be tautological and could never fail; Task 4's first cut of the
/// resolver's own collision guard shipped exactly that mistake and stayed green while silently
/// deleting 186 framework members.
/// </summary>
[TestFixture]
public class NetNameManglerTests
{
    private const string DefaultDeclaringType = "Contoso.Widget";

    private static NetTypeResolver FrameworkOnly() =>
        NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths);

    private static NetMemberDescriptor Member(
        string name, int arity, params (string Type, NetRefKind RefKind)[] parameters) =>
        Member(DefaultDeclaringType, name, arity, parameters);

    private static NetMemberDescriptor Member(
        string declaringType, string name, int arity, params (string Type, NetRefKind RefKind)[] parameters) =>
        new NetMemberDescriptor(
            name, declaringType, NetMemberCategory.Method, isStatic: false, arity,
            typeFullName: "System.Void",
            parameters: parameters.Select(p => new NetParameterDescriptor(p.RefKind, p.Type)).ToList());

    /// <summary>
    /// The oracle every distinctness test in this file checks against. Deliberately built with its
    /// OWN separators and OWN string formatting — never by calling
    /// <see cref="NetNameMangler.Mangle"/> or anything inside it — so a test comparing two members'
    /// oracle keys is a genuine, independent statement about whether the CLR treats them as the
    /// same signature, not a restatement of whatever the mangler happens to compute.
    /// </summary>
    private static string IndependentSignatureKey(NetMemberDescriptor m) =>
        string.Join("|",
            m.DeclaringTypeFullName,
            m.Kind.ToString(),
            m.Name,
            m.IsStatic.ToString(),
            m.Arity.ToString(),
            string.Join(";", m.Parameters.Select(p => p.RefKind + ":" + p.TypeFullName)));

    /// <summary>
    /// Asserts every member in <paramref name="members"/> mangles to a distinct name, guarded by
    /// first proving the fixture itself is a genuine overload set (no two members share an
    /// independently-derived signature identity) — otherwise a fixture bug could make this pass
    /// for the wrong reason.
    /// </summary>
    private static void AssertAllDistinctAgainstIndependentOracle(IReadOnlyList<NetMemberDescriptor> members)
    {
        var oracleKeys = members.Select(IndependentSignatureKey).ToList();
        Assert.That(oracleKeys.Distinct().Count(), Is.EqualTo(members.Count),
            "Test fixture bug: two members in this set share an INDEPENDENTLY-derived signature " +
            "identity (declaring type + kind + name + static-ness + arity + per-parameter " +
            "refkind/type), so this test cannot prove anything about NetNameMangler. Fix the " +
            "fixture, not the mangler.");

        var mangled = members.Select(NetNameMangler.Mangle).ToList();
        Assert.That(mangled.Distinct().Count(), Is.EqualTo(members.Count),
            "NetNameMangler produced the SAME export name for two members whose " +
            "independently-derived signature identities differ. Two distinct .NET members would " +
            "share one BlnetProxyTable slot (Task 12) and one shim export (Task 14); calls would " +
            "silently reach the wrong member. Mangled names: " + string.Join(", ", mangled));
    }

    // ------------------------------------------------------------------
    // Determinism
    // ------------------------------------------------------------------

    [Test]
    public void MangleIsDeterministicAcrossCalls()
    {
        // Two SEPARATE NetMemberDescriptor instances (including two separate Parameters list
        // instances) with identical field VALUES. NetMemberDescriptor is deliberately not a
        // record (reference equality on Parameters), so this also proves Mangle does not
        // accidentally key off object identity.
        NetMemberDescriptor Build() => new NetMemberDescriptor(
            "Match", "System.Text.RegularExpressions.Regex", NetMemberCategory.Method,
            isStatic: true, arity: 0, typeFullName: "System.Text.RegularExpressions.Match",
            parameters: new[] { new NetParameterDescriptor(NetRefKind.None, "System.String") });

        var first = NetNameMangler.Mangle(Build());
        var second = NetNameMangler.Mangle(Build());

        Assert.That(second, Is.EqualTo(first),
            "NetNameMangler.Mangle must be a pure function of VALUES. Two structurally-identical " +
            "but reference-distinct descriptors produced two different names. Task 15's cache " +
            "key hashes this output — instability here means a stale shim can ship silently.");
    }

    [Test]
    public void MangleIsStableAcrossRepeatedCallsOnTheSameInstance()
    {
        var member = Member("Foo", 0, ("System.Int32", NetRefKind.None));

        var results = Enumerable.Range(0, 5).Select(_ => NetNameMangler.Mangle(member)).Distinct().ToList();

        Assert.That(results, Has.Count.EqualTo(1),
            "Repeated calls with the SAME descriptor instance produced different names -- " +
            "Mangle must have no ambient state (a counter, a cache, anything) that varies " +
            "call-to-call.");
    }

    [Test]
    public void MangleIsIndependentOfWhatElseWasMangledBeforeOrAfter()
    {
        NetMemberDescriptor[] Fresh() => new[]
        {
            Member("Alpha", 0, ("System.Int32", NetRefKind.None)),
            Member("Beta", 0, ("System.String", NetRefKind.None)),
            Member("Gamma", 1, ("System.Boolean", NetRefKind.Ref)),
        };

        var forward = Fresh().Select(NetNameMangler.Mangle).ToList();
        var backward = Fresh().Reverse().Select(NetNameMangler.Mangle).ToList();

        Assert.That(backward, Is.EqualTo(Enumerable.Reverse(forward).ToList()),
            "Mangling the same three members in the OPPOSITE order produced different results " +
            "for at least one of them. Mangle must depend only on the descriptor passed to it, " +
            "never on prior calls or where a member falls in a batch -- a dependency on order " +
            "would break Task 15's cache key on any surface reordering (a new NuGet package " +
            "version, an assembly recompiled with members in a different metadata-table order).");
    }

    // ------------------------------------------------------------------
    // Collision-freedom over the fully-qualified declaring type -- §7.3's explicit trap
    // ------------------------------------------------------------------

    [Test]
    public void CollisionFreedomOverFullyQualifiedDeclaringTypes()
    {
        var a = Member("MyLib.Customer", "Recalculate", 0, ("MyLib.Order", NetRefKind.None));
        var b = Member("OtherLib.Customer", "Recalculate", 0, ("MyLib.Order", NetRefKind.None));

        Assert.That(NetNameMangler.Mangle(a), Is.Not.EqualTo(NetNameMangler.Mangle(b)),
            "§7.3's own worked example: MyLib.Customer.Recalculate and OtherLib.Customer." +
            "Recalculate share a short type name and an otherwise identical signature. A " +
            "mangler keyed on the SHORT declaring-type name would collide them into one export " +
            "slot. NetNameMangler must incorporate the FULLY-QUALIFIED declaring type.");
    }

    [Test]
    public void NaiveIllegalCharacterReplacementIsNotACollisionSource()
    {
        // "A.B" and a (deliberately unusual but not impossible) literal "A_B" declaring type both
        // sanitize to "A_B" under a naive dots-to-underscores replacement. NetNameMangler must
        // not rely on the sanitized stem for uniqueness.
        var dotted = Member("A.B", "Foo", 0, ("System.Int32", NetRefKind.None));
        var underscored = Member("A_B", "Foo", 0, ("System.Int32", NetRefKind.None));

        Assert.That(NetNameMangler.Mangle(dotted), Is.Not.EqualTo(NetNameMangler.Mangle(underscored)),
            "Declaring types 'A.B' and 'A_B' produced the same export name. A naive " +
            "'replace-illegal-characters-with-underscore' scheme collapses the two -- the hash " +
            "suffix must be computed over the RAW, unsanitized identity, not the sanitized stem.");
    }

    // ------------------------------------------------------------------
    // Distinctness across an overload set -- synthetic, covering arity and ref-kind
    // ------------------------------------------------------------------

    [Test]
    public void DistinctnessAcrossASyntheticOverloadSet()
    {
        var members = new[]
        {
            Member("Foo", 0),                                                              // no params
            Member("Foo", 0, ("System.Int32", NetRefKind.None)),
            Member("Foo", 1, ("System.Int32", NetRefKind.None)),                            // arity axis
            Member("Foo", 0, ("System.Int32", NetRefKind.Ref)),                             // refkind axis
            Member("Foo", 0, ("System.Int32", NetRefKind.Out)),
            Member("Foo", 0, ("System.Int32", NetRefKind.In)),
            Member("Foo", 0, ("System.Int32", NetRefKind.None), ("System.String", NetRefKind.None)),
            Member("Foo", 0, ("System.String", NetRefKind.None), ("System.Int32", NetRefKind.None)), // param order
        };

        AssertAllDistinctAgainstIndependentOracle(members);
    }

    [Test]
    public void DistinctnessSurvivesAcrossDifferentMemberKindsAndStaticness()
    {
        var method = new NetMemberDescriptor("Count", DefaultDeclaringType, NetMemberCategory.Method,
            isStatic: false, arity: 0, typeFullName: "System.Int32", parameters: Array.Empty<NetParameterDescriptor>());
        var property = new NetMemberDescriptor("Count", DefaultDeclaringType, NetMemberCategory.Property,
            isStatic: false, arity: 0, typeFullName: "System.Int32", parameters: Array.Empty<NetParameterDescriptor>());
        var field = new NetMemberDescriptor("Count", DefaultDeclaringType, NetMemberCategory.Field,
            isStatic: false, arity: 0, typeFullName: "System.Int32", parameters: Array.Empty<NetParameterDescriptor>());
        var staticMethod = new NetMemberDescriptor("Count", DefaultDeclaringType, NetMemberCategory.Method,
            isStatic: true, arity: 0, typeFullName: "System.Int32", parameters: Array.Empty<NetParameterDescriptor>());

        AssertAllDistinctAgainstIndependentOracle(new[] { method, property, field, staticMethod });
    }

    [Test]
    public void ConstructorsMangleDistinctlyFromEachOtherAndFromOrdinaryMembers()
    {
        var ctorNoArgs = new NetMemberDescriptor(".ctor", DefaultDeclaringType, NetMemberCategory.Constructor,
            isStatic: false, arity: 0, typeFullName: "System.Void", parameters: Array.Empty<NetParameterDescriptor>());
        var ctorOneArg = new NetMemberDescriptor(".ctor", DefaultDeclaringType, NetMemberCategory.Constructor,
            isStatic: false, arity: 0, typeFullName: "System.Void",
            parameters: new[] { new NetParameterDescriptor(NetRefKind.None, "System.Int32") });
        // A (hypothetical, IL-legal) ordinary member literally named "ctor" -- not ".ctor" -- must
        // not collide with the constructor whose readable spelling is also "ctor".
        var memberNamedCtor = new NetMemberDescriptor("ctor", DefaultDeclaringType, NetMemberCategory.Method,
            isStatic: false, arity: 0, typeFullName: "System.Void", parameters: Array.Empty<NetParameterDescriptor>());

        AssertAllDistinctAgainstIndependentOracle(new[] { ctorNoArgs, ctorOneArg, memberNamedCtor });
    }

    // ------------------------------------------------------------------
    // Distinctness across REAL framework overload sets -- the exact pairs the plan's review found
    // ------------------------------------------------------------------

    [Test]
    public void RealFramework_TaskFromException_GenericArityPairManglesDistinctly()
    {
        var members = FrameworkOnly().GetMembers("System.Threading.Tasks.Task")
            .Where(m => m.Name == "FromException" && m.Kind == NetMemberCategory.Method)
            .ToList();

        Assert.That(members.Select(m => m.Arity), Is.EquivalentTo(new[] { 0, 1 }),
            "Guard: this test needs the real Task.FromException(Exception) / " +
            "FromException<T>(Exception) pair. If GetMembers stopped returning both, that is a " +
            "NetTypeResolver regression, not a NetNameMangler one.");

        AssertAllDistinctAgainstIndependentOracle(members);
    }

    [Test]
    public void RealFramework_EventSourceWrite_RefKindPairManglesDistinctly()
    {
        var members = FrameworkOnly().GetMembers("System.Diagnostics.Tracing.EventSource")
            .Where(m => m.Name == "Write" && m.Arity == 1 && m.Parameters.Count == 3)
            .ToList();

        Assert.That(members.Count, Is.EqualTo(2),
            "Guard: this test needs the real EventSource.Write(String, EventSourceOptions, T) / " +
            "Write(String, ref EventSourceOptions, ref T) pair, which differ ONLY by " +
            "parameter ref-kind.");

        AssertAllDistinctAgainstIndependentOracle(members);
    }

    [Test]
    public void RealFramework_IQueryProviderOverloads_MangleDistinctly()
    {
        var members = FrameworkOnly().GetMembers("System.Linq.IQueryProvider").ToList();

        Assert.That(members.Count, Is.GreaterThanOrEqualTo(4),
            "Guard: IQueryProvider should expose CreateQuery/CreateQuery<TElement> and " +
            "Execute/Execute<TResult> -- four members total.");

        AssertAllDistinctAgainstIndependentOracle(members);
    }

    // ------------------------------------------------------------------
    // Legal C identifier
    // ------------------------------------------------------------------

    // A loop rather than [TestCaseSource]: NUnit1026 requires a [Test] method to be public, and a
    // public method cannot take NetMemberDescriptor as a parameter -- it is internal to
    // BasicLang, and a public member cannot expose a less-accessible type even across an
    // InternalsVisibleTo boundary (CS0051). Assert.Multiple reports every failing fixture in one
    // run rather than stopping at the first.
    [Test]
    public void OutputIsALegalCIdentifier()
    {
        Assert.Multiple(() =>
        {
            foreach (var member in ExoticDescriptors())
            {
                var mangled = NetNameMangler.Mangle(member);
                Assert.That(mangled, Does.Match("^[A-Za-z_][A-Za-z0-9_]*$"),
                    $"'{mangled}' is not a legal C identifier for member {member}. A .NET name " +
                    "can contain '.', '`', '<', '>', '+', '[', ']', ',', '*', '&' and none of " +
                    "those may survive into the mangled output.");
            }
        });
    }

    private static IEnumerable<NetMemberDescriptor> ExoticDescriptors()
    {
        // Generic type argument in the declaring type: '<', '>', ','.
        yield return new NetMemberDescriptor("Add",
            "System.Collections.Generic.Dictionary<System.String, System.Int32>",
            NetMemberCategory.Method, isStatic: false, arity: 0, typeFullName: "System.Void",
            parameters: new[]
            {
                new NetParameterDescriptor(NetRefKind.None, "System.String"),
                new NetParameterDescriptor(NetRefKind.None, "System.Int32"),
            });

        // Array and multi-dimensional array parameters: '[', ']', ','.
        yield return new NetMemberDescriptor("Copy", DefaultDeclaringType, NetMemberCategory.Method,
            isStatic: true, arity: 0, typeFullName: "System.Void",
            parameters: new[]
            {
                new NetParameterDescriptor(NetRefKind.None, "System.String[]"),
                new NetParameterDescriptor(NetRefKind.None, "System.Int32[,]"),
            });

        // Tuple (ValueTuple) return/parameter spelling: '<', '>', ','.
        yield return new NetMemberDescriptor("SinCos", DefaultDeclaringType, NetMemberCategory.Method,
            isStatic: true, arity: 0,
            typeFullName: "System.ValueTuple<System.Double, System.Double>",
            parameters: new[] { new NetParameterDescriptor(NetRefKind.None, "System.Double") });

        // Nested-type spelling with '+' (CLR metadata form; this resolver normally emits '.', but
        // NetMemberDescriptor is a plain data holder and nothing enforces that upstream).
        yield return new NetMemberDescriptor("Frobnicate", "Contoso+Nested", NetMemberCategory.Method,
            isStatic: false, arity: 0, typeFullName: "System.Void", parameters: Array.Empty<NetParameterDescriptor>());

        // Pointer and byref-style ('&') type spellings.
        yield return new NetMemberDescriptor("Poke", DefaultDeclaringType, NetMemberCategory.Method,
            isStatic: false, arity: 0, typeFullName: "System.Void",
            parameters: new[]
            {
                new NetParameterDescriptor(NetRefKind.None, "System.Int32*"),
                new NetParameterDescriptor(NetRefKind.RefReadOnly, "System.Int32&"),
            });

        // Backtick-suffixed open-generic declaring type, generic method (arity > 0), mixed
        // ref-kinds, and a constructor.
        yield return new NetMemberDescriptor("Parse", "System.Collections.Generic.List`1",
            NetMemberCategory.Method, isStatic: true, arity: 2, typeFullName: "System.Boolean",
            parameters: new[]
            {
                new NetParameterDescriptor(NetRefKind.None, "System.String"),
                new NetParameterDescriptor(NetRefKind.Out, "System.Int32"),
            });
        yield return new NetMemberDescriptor(".ctor", "System.Collections.Generic.List`1",
            NetMemberCategory.Constructor, isStatic: false, arity: 0, typeFullName: "System.Void",
            parameters: new[] { new NetParameterDescriptor(NetRefKind.None, "System.Int32") });

        // Empty declaring type / member name segments (defensive -- should never occur from the
        // resolver, but Sanitize/Mangle must not produce an illegal result if it did).
        yield return new NetMemberDescriptor("", DefaultDeclaringType, NetMemberCategory.Field,
            isStatic: false, arity: 0, typeFullName: "System.Void", parameters: Array.Empty<NetParameterDescriptor>());
    }

    // ------------------------------------------------------------------
    // Length cap and truncation
    // ------------------------------------------------------------------

    [Test]
    public void OutputNeverExceedsMaxIdentifierLength()
    {
        var longType = string.Concat(Enumerable.Repeat("VeryLongNamespaceSegment.", 60)) + "Widget";
        var member = Member(longType, "SomeMethodWithAFairlyLongNameToo", 0,
            ("System.String", NetRefKind.None), ("System.Int32", NetRefKind.None));

        var mangled = NetNameMangler.Mangle(member);

        Assert.That(mangled.Length, Is.LessThanOrEqualTo(NetNameMangler.MaxIdentifierLength),
            $"Mangled name is {mangled.Length} characters, over the documented cap of " +
            $"{NetNameMangler.MaxIdentifierLength}.");
        Assert.That(mangled, Does.Match("^[A-Za-z_][A-Za-z0-9_]*$"),
            "A truncated name must still be a legal C identifier.");
    }

    [Test]
    public void TruncationNeverReintroducesACollision()
    {
        // Two declaring types identical for far more than NetNameMangler.MaxIdentifierLength
        // characters, diverging only at the very end -- past where the readable stem gets cut.
        var sharedPrefix = string.Concat(Enumerable.Repeat("VeryLongNamespaceSegment.", 40));
        var a = Member(sharedPrefix + "TypeA", "Foo", 0, ("System.Int32", NetRefKind.None));
        var b = Member(sharedPrefix + "TypeB", "Foo", 0, ("System.Int32", NetRefKind.None));

        Assume.That(sharedPrefix.Length, Is.GreaterThan(NetNameMangler.MaxIdentifierLength),
            "Guard: the shared prefix must itself exceed the cap, or this test does not " +
            "actually exercise truncation.");

        var mangledA = NetNameMangler.Mangle(a);
        var mangledB = NetNameMangler.Mangle(b);

        Assert.That(mangledA, Is.Not.EqualTo(mangledB),
            "Two declaring types that share an identical prefix well past the truncation cap, " +
            "and differ only after it, mangled to the SAME name. The hash suffix must be " +
            "computed from the FULL untruncated identity, not from the truncated stem -- " +
            "otherwise truncation reintroduces exactly the collision the hash exists to " +
            "prevent.");
    }

    // ------------------------------------------------------------------
    // Misuse
    // ------------------------------------------------------------------

    [Test]
    public void MangleOfNullThrows() =>
        Assert.That(() => NetNameMangler.Mangle(null), Throws.ArgumentNullException);

    // ------------------------------------------------------------------
    // Corpus sweep -- real evidence over a real, diverse slice of the framework surface
    // ------------------------------------------------------------------

    private static readonly string[] CorpusTypeNames =
    {
        "System.Text.RegularExpressions.Regex",
        "System.Text.RegularExpressions.Match",
        "System.Text.RegularExpressions.MatchCollection",
        "System.Threading.Tasks.Task",
        "System.Threading.Tasks.Task`1",
        "System.Threading.Tasks.ValueTask",
        "System.Text.StringBuilder",
        "System.IO.FileStream",
        "System.IO.Stream",
        "System.IO.Path",
        "System.IO.File",
        "System.IO.Directory",
        "System.Diagnostics.Tracing.EventSource",
        "System.Linq.Expressions.Expression",
        "System.Linq.IQueryProvider",
        "System.Linq.Enumerable",
        "System.Math",
        "System.Console",
        "System.String",
        "System.DateTime",
        "System.DateTimeOffset",
        "System.TimeSpan",
        "System.Guid",
        "System.Uri",
        "System.Exception",
        "System.ArgumentException",
        "System.InvalidOperationException",
        "System.Random",
        "System.Environment",
        "System.Convert",
        "System.Array",
        "System.Text.Encoding",
        "System.Collections.Generic.List`1",
        "System.Collections.Generic.Dictionary`2",
        "System.Collections.Generic.HashSet`1",
        "System.Collections.Generic.Queue`1",
        "System.Collections.Generic.Stack`1",
        "System.Collections.Specialized.NameValueCollection",
        "System.Threading.CancellationToken",
        "System.Threading.CancellationTokenSource",
        "System.Runtime.InteropServices.Marshal",
        "System.Nullable`1",
        "System.ValueTuple`2",
    };

    /// <summary>
    /// Mangles every member <see cref="NetTypeResolver.GetMembers"/> reports for a curated,
    /// diverse ~40-type slice of the real framework surface (chosen to include the exact types
    /// the plan's review named as containing generic-arity or ref-kind collisions:
    /// <c>Task.FromException</c>, <c>EventSource.Write</c>, <c>Expression.Lambda</c>,
    /// <c>IQueryProvider</c>, <c>Marshal</c>) and asserts every mangled name is unique across the
    /// WHOLE combined set, not just within one type.
    /// </summary>
    [Test]
    public void CorpusSweepAcrossRealFrameworkTypesProducesNoCollisions()
    {
        var resolver = FrameworkOnly();
        var totalMembers = 0;
        var typesWithMembers = 0;

        // Keyed on the INDEPENDENT oracle, not on anything the mangler computes. This corpus
        // deliberately includes overlapping type hierarchies (Task and Task`1; Exception,
        // ArgumentException and InvalidOperationException; Stream and its MarshalByRefObject
        // base) BECAUSE that is a real, common shape of a .NET reference closure -- so the SAME
        // real member (e.g. System.Exception.ToString()) is legitimately reached more than once,
        // once directly and again as an inherited member reported under its true declaring type
        // (NetTypeResolver.GetMembers's own contract). That is not a mangler collision -- it is
        // the same member, correctly mangled the same way both times -- so members are deduped by
        // independent signature identity BEFORE checking whether two DIFFERENT identities ever
        // produced the same export name.
        var distinctByOracle = new Dictionary<string, NetMemberDescriptor>(StringComparer.Ordinal);

        foreach (var typeName in CorpusTypeNames)
        {
            var members = resolver.GetMembers(typeName);
            if (members.Count == 0)
                continue;
            typesWithMembers++;

            foreach (var member in members)
            {
                totalMembers++;
                distinctByOracle[IndependentSignatureKey(member)] = member;
            }
        }

        Assert.That(typesWithMembers, Is.EqualTo(CorpusTypeNames.Length),
            "One or more corpus type names failed to resolve against the framework closure -- " +
            "fix the spelling in CorpusTypeNames. A type that never resolved contributes no " +
            "members, so this sweep would silently prove nothing for it.");
        Assert.That(totalMembers, Is.GreaterThan(500),
            $"The {CorpusTypeNames.Length}-type corpus produced only {totalMembers} raw member " +
            "entries -- suspiciously few for this list. Widen CorpusTypeNames or investigate a " +
            "resolver regression before trusting this sweep's zero-collisions result.");
        Assert.That(distinctByOracle.Count, Is.GreaterThan(400),
            $"Only {distinctByOracle.Count} of {totalMembers} raw entries were independently " +
            "distinct signatures -- suspiciously heavy overlap for this corpus. Investigate " +
            "before trusting the zero-collisions result below.");

        var mangledToOracleKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var collisions = new List<string>();
        foreach (var (oracleKey, member) in distinctByOracle)
        {
            var mangled = NetNameMangler.Mangle(member);
            if (mangledToOracleKey.TryGetValue(mangled, out var existingOracleKey))
                collisions.Add($"'{mangled}' <- both [{existingOracleKey}] AND [{oracleKey}]");
            else
                mangledToOracleKey[mangled] = oracleKey;
        }

        Assert.That(collisions, Is.Empty,
            $"NetNameMangler produced the SAME export name for {collisions.Count} pair(s) of " +
            "INDEPENDENTLY-distinct real framework members (different declaring type, kind, " +
            $"name, static-ness, arity or parameter refkind/type) across a " +
            $"{CorpusTypeNames.Length}-type, {distinctByOracle.Count}-distinct-member corpus:\n" +
            string.Join("\n", collisions));

        TestContext.WriteLine(
            $"NetNameMangler corpus sweep: {CorpusTypeNames.Length} types, {totalMembers} raw " +
            $"member entries, {distinctByOracle.Count} independently-distinct signatures, " +
            $"{collisions.Count} collisions.");
    }
}
