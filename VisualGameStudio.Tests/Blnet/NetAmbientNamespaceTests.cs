using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Net;
using NUnit.Framework;
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;
using TypeKind = BasicLang.Compiler.SemanticAnalysis.TypeKind;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Spec §12.4: the ambient namespace set (§6.5) used by <see cref="NetTypeResolver"/> ≡ the one
/// used by the C# backend (<see cref="ImprovedCSharpCodeGenerator"/> — the type in the file
/// <c>CSharpBackend.cs</c>; there is no type literally named <c>CSharpBackend</c> anywhere in the
/// codebase). Without this, spec §6.3's "valid programs behave identically on both backends" is
/// false: a namespace the C# backend auto-imports but the resolver does not know about means a
/// program that compiles on the C# backend becomes a BL6016 natively.
///
/// <para><b>What this fixture used to be, and why it changed (P2a-2 Task 14).</b> The equality was
/// held by <c>CSharpBackendAndResolverShareOneAmbientSet</c>, which asserted
/// <c>ImprovedCSharpCodeGenerator.AmbientNamespacesForTest Is.EquivalentTo
/// NetAmbientNamespaces.All</c> — while that member was defined as
/// <c>=&gt; NetAmbientNamespaces.All</c>. It compared the constant with itself. It could not fail
/// under any edit to the backend's seeding loop or the analyzer's candidate loop, including
/// deleting either outright, and only 3 of the 17 namespaces were exercised behaviourally
/// anywhere. Both halves are now asserted against what the two consumers DO:</para>
/// <list type="bullet">
///   <item><description><b>Resolver half</b> — <see cref="BareNameResolvesThroughItsAmbientNamespace"/>
///     compiles <c>Dim x As &lt;BareType&gt;</c> on the native path, once per ambient namespace,
///     with an UNCLAIMED framework type that lives only in that namespace. A namespace missing
///     from <see cref="NetAmbientNamespaces.All"/> makes its row draw BL6016.</description></item>
///   <item><description><b>Backend half</b> — <see cref="GenerateSeedsEveryAmbientNamespaceIntoItsCandidateUsings"/>
///     runs <c>Generate</c> and reads the candidate <c>using</c> set the generator instance
///     actually built. (This required the one product change in this batch: the test seam was
///     turned from a static alias for the constant into an instance view of <c>_usings</c>.)
///     </description></item>
/// </list>
/// </summary>
[TestFixture]
public class NetAmbientNamespaceTests
{
    /// <summary>One resolver for the assembly — construction reads ~170 assemblies.</summary>
    private static readonly Lazy<NetTypeResolver> SharedResolver = NetStubHarness.SharedResolver;

    // ------------------------------------------------------------------------------------
    // The probe table — one UNCLAIMED, non-generic framework type per ambient namespace.
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// One row per ambient namespace: a type that (1) lives ONLY in that namespace among the 17,
    /// so removing the namespace makes the bare name unresolvable; (2) is NOT claimed by §6.5's
    /// predicate, so the probe actually runs (a claimed name returns from
    /// <c>ProbeNetTypeReference</c> before any lookup and would make the row vacuous — asserted
    /// per row, not assumed); and (3) is non-generic, because a bare spelling carries arity 0.
    ///
    /// <para><b>Every entry below was measured, not chosen from memory.</b> Several plausible
    /// candidates are unusable for reasons unrelated to the ambient set — anything under
    /// <c>System.Collections.Generic</c> is almost entirely generic, and a claimed name
    /// (<c>List</c>, <c>Task</c>, <c>Func</c>, <c>Action</c>, <c>StringBuilder</c>, the P1 six)
    /// can never serve as a probe at all.</para>
    /// </summary>
    private static readonly (string Namespace, string BareType)[] AmbientProbes =
    {
        ("System", "TimeZoneInfo"),
        ("System.Collections.Generic", "ReferenceEqualityComparer"),
        ("System.Threading.Tasks", "Parallel"),
        ("System.Collections", "BitArray"),
        ("System.Runtime.InteropServices", "Marshal"),
        ("System.Text", "Encoding"),
        ("System.IO", "MemoryStream"),
        ("System.Linq", "Enumerable"),
        ("System.Net", "IPAddress"),
        ("System.Net.Http", "HttpClient"),
        ("System.Net.Sockets", "TcpClient"),
        ("System.Text.Json", "JsonSerializer"),
        ("System.Text.Json.Nodes", "JsonNode"),
        ("System.Text.RegularExpressions", "MatchCollection"),
        ("System.Security.Cryptography", "RandomNumberGenerator"),
        ("System.Diagnostics", "Stopwatch"),
        ("System.Threading", "Mutex"),
    };

    private static IEnumerable<TestCaseData> AmbientProbeCases() =>
        AmbientProbes.Select(p => new TestCaseData(p.Namespace, p.BareType)
            .SetName($"BareNameResolvesThroughItsAmbientNamespace({p.Namespace} -> {p.BareType})"));

    /// <summary>
    /// The table is DERIVED from the constant, so a namespace added to
    /// <see cref="NetAmbientNamespaces.All"/> without a probe fails here rather than joining the
    /// set untested — and a namespace removed from the constant fails here too, alongside its own
    /// probe row.
    /// </summary>
    [Test]
    public void EveryAmbientNamespaceHasABehaviouralProbe()
    {
        Assert.That(AmbientProbes.Select(p => p.Namespace), Is.EquivalentTo(NetAmbientNamespaces.All),
            "the ambient set and this fixture's probe table drifted. Add a row naming an "
            + "UNCLAIMED, non-generic type that lives only in the new namespace (and explain it "
            + "in the table's remarks) — do not delete the row for a namespace you removed "
            + "without saying why the namespace went.");
        Assert.That(NetAmbientNamespaces.All, Has.Length.EqualTo(17),
            "NetAmbientNamespaces.All no longer has 17 entries. This is not necessarily wrong, "
            + "but update this assertion (and explain the change in NetAmbientNamespaces.cs) "
            + "rather than silently accepting a different count — the C# backend and "
            + "NetTypeResolver both depend on this exact set.");
    }

    /// <summary>
    /// §6.5 step 2, behaviourally: with NO <c>Using</c> directive, a bare framework type name
    /// resolves through the ambient set on the NATIVE path and draws no finding. Remove the
    /// namespace from <see cref="NetAmbientNamespaces.All"/> and this row's lookup exhausts every
    /// remaining candidate and reports BL6016 — which is exactly the §6.3 divergence the shared
    /// constant exists to prevent, since the C# backend would still have compiled the program.
    /// </summary>
    [TestCaseSource(nameof(AmbientProbeCases))]
    public void BareNameResolvesThroughItsAmbientNamespace(string ambientNamespace, string bareType)
    {
        Assert.That(NetClaimPredicate.IsClaimedTypeName(bareType), Is.False,
            $"guard: '{bareType}' became CLAIMED, so ProbeNetTypeReference returns before any "
            + "lookup and this row would pass no matter what the ambient set contains. Pick "
            + $"another unclaimed type in {ambientNamespace}.");

        var analyzer = Analyze($"""
            Module M
             Sub Main()
              Dim x As {bareType}
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        Assert.That(analyzer.NetDiagnostics, Is.Empty,
            $"'{bareType}' did not resolve with '{ambientNamespace}' ambient. On the native path "
            + "that is a BUILD ERROR after the flip, on a program the C# backend compiles fine — "
            + "spec §6.3's equal-behavior claim. Fix NetAmbientNamespaces.All (the one shared "
            + "constant) or SemanticAnalyzer.NetCandidateNames, not this test. Got: "
            + string.Join(" | ", analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
    }

    /// <summary>
    /// The BACKEND half of §12.4's ambient invariant, asserted against what
    /// <c>Generate</c> actually seeded into its candidate <c>using</c> set rather than against
    /// the constant (see the fixture remarks for the tautology this replaced). A superset, not an
    /// equality: <c>Generate</c> also adds the program's own <c>Using</c> directives and every
    /// stdlib-required import, so equality would be false for any non-trivial program.
    /// </summary>
    [Test]
    public void GenerateSeedsEveryAmbientNamespaceIntoItsCandidateUsings()
    {
        var module = new IRModule("AmbientProbeModule");
        var main = new IRFunction("Main", new TypeInfo("Void", TypeKind.Void));
        var entry = new BasicBlock("entry");
        entry.AddInstruction(new IRReturn());
        main.Blocks.Add(entry);
        main.EntryBlock = entry;
        module.Functions.Add(main);

        var generator = new ImprovedCSharpCodeGenerator();
        generator.Generate(module);

        Assert.That(generator.CandidateUsingsForTest, Is.SupersetOf(NetAmbientNamespaces.All),
            "the C# backend's candidate using set no longer contains every ambient namespace. "
            + "Generate must seed them from NetAmbientNamespaces.All (CSharpBackend.cs, the "
            + "`foreach (var ambientNamespace in NetAmbientNamespaces.All)` loop) — a parallel "
            + "list, or a dropped loop, means a name the resolver believes is ambient stops "
            + "being ambient on the C# side and §6.3's equal-behavior claim is false.");
    }

    // ------------------------------------------------------------------------------------

    private static SemanticAnalyzer Analyze(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => SharedResolver.Value, nativeBackend: true);
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));
        return analyzer;
    }
}
