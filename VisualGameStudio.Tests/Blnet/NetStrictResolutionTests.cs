using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicLang;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.ProjectSystem;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Net;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// P2a-2 Task 4 — strict resolution, warning-only on BOTH backends (§6.3's severity split is
/// Task 5's flip; nothing here may fail a build).
///
/// <para><b>The argument side (§6.5).</b> A call whose callee the member probe RESOLVED is
/// judged by REAL C# overload resolution (<c>NetTypeResolver.ResolveOverload</c> — its first
/// production wiring): the winner replaces the Task-2 name-only annotation, an ambiguous call
/// is BL6018, a no-match is BL6017's "no matching overload" half with the argument types in the
/// message, and a user-defined BL class argument is BL6019. Shapes the analyzer cannot type
/// (<c>Nothing</c>, Object-degraded expressions, lambdas, method-level generic arguments) are
/// deliberately left name-only recorded with NO manufactured finding — the probe's own contract
/// is "a caller that cannot type every argument must not ask".</para>
///
/// <para><b>D-P1 (§14.15, Step 2a).</b> <c>ToString()</c>/<c>GetHashCode()</c> are admitted
/// from <c>System.Object</c> for every non-overriding type; <c>GetType()</c> and
/// <c>Equals(Object)</c> stay excluded. The probe's <c>ObjectMemberNames</c> early-out is
/// lifted for exactly the two names, so they annotate and will reach the surface.</para>
///
/// <para><b>§11.1 ladder-trigger completion.</b> A catch clause whose type RESOLVES as a .NET
/// exception outside <c>CppExceptionTypes</c>' 12-name set carries the resolver's FQ name into
/// IR and gets its own NetException ladder arm.</para>
///
/// <para><b>D-P2 (§15.11).</b> A resolved reference whose <c>TargetFrameworkAttribute</c> names
/// .NETCoreApp ≥ 9.0 draws a BL6021 warning naming the TFM and the pinned net8.0 shim.</para>
/// </summary>
[TestFixture]
public class NetStrictResolutionTests
{
    /// <summary>One resolver for the fixture — construction reads ~170 assemblies.</summary>
    private static readonly Lazy<NetTypeResolver> SharedResolver =
        new(() => NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths));

    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "blnet-strict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------

    private static SemanticAnalyzer Analyze(
        string source, bool nativeBackend = true, Func<NetTypeResolver> resolverFactory = null)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(
            resolverFactory ?? (() => SharedResolver.Value), nativeBackend);
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));
        return analyzer;
    }

    private static string CompileToCppWithResolver(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => SharedResolver.Value, nativeBackend: true);
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));

        var module = new IRBuilder(analyzer).Build(ast, "TestModule");
        return new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(module);
    }

    private static List<string> FindingCodes(SemanticAnalyzer analyzer) =>
        analyzer.NetDiagnostics.Select(d => d.Code).ToList();

    private string EmitProbeAssembly(string name, string source)
    {
        var path = Path.Combine(_dir, name + ".dll");
        var compilation = CSharpCompilation.Create(
            name,
            new[] { CSharpSyntaxTree.ParseText(source) },
            NetTypeResolverTestRefs.FrameworkPaths.Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var emit = compilation.Emit(path);
        Assert.That(emit.Success, Is.True,
            "the FIXTURE failed to build its probe assembly: " + string.Join("\n",
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    // ====================================================================================
    // §6.5 argument side — ResolveOverload wired at the call sites
    // ====================================================================================

    /// <summary>
    /// The winner REPLACES the name-only Task-2 record. The 3-argument shape is the mutation
    /// oracle: the first name-match in metadata order is the 1-parameter overload, so a
    /// reverted (name-only) recording pins Parameters.Count == 1 and this goes red.
    /// </summary>
    [Test]
    public void ResolvableStaticOverload_RecordsTheWinningDescriptor()
    {
        // The plan suggests Guid.TryParse — but Guid is claimed by §6.5 row (a) (NativeOwned),
        // so no probe may ever judge it; Convert.ToBase64String is claim-free (row (c) is
        // per-call and Convert has no EmitStdLibCall arm for it). Verified here, not assumed.
        Assert.That(NetClaimPredicate.IsClaimedCall("Guid", "TryParse"), Is.True,
            "guard: Guid is NativeOwned (§6.5 row (a)) — if this ever flips, the plan's "
            + "suggested member became probeable and this fixture should widen.");
        Assert.That(NetClaimPredicate.IsClaimedCall("Convert", "ToBase64String"), Is.False,
            "guard: the member under test must be UNCLAIMED or the probe never runs.");

        var analyzer = Analyze("""
            Module M
             Sub Main()
              Dim data(3) As Byte
              Dim s = Convert.ToBase64String(data, 0, 2)
             End Sub
            End Module
            """);

        Assert.That(FindingCodes(analyzer), Is.Empty,
            "a resolvable overload drew a finding: "
            + string.Join(" | ", analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));

        var recorded = analyzer.NetResolvedMembers.Values
            .Where(m => m.Name == "ToBase64String").ToList();
        Assert.That(recorded, Has.Count.EqualTo(1),
            "exactly one call site, so exactly one annotation");
        Assert.That(recorded[0].Parameters.Select(p => p.TypeFullName),
            Is.EqualTo(new[] { "System.Byte[]", "System.Int32", "System.Int32" }),
            "the annotation must carry THE WINNING overload (byte[], int, int) — a "
            + "name-only record keeps whichever member metadata order lists first. FIX "
            + "SemanticAnalyzer.ProbeNetInvocation's Resolved arm.");
        Assert.That(recorded[0].IsStatic, Is.True);
    }

    [Test]
    public void ResolvableInstanceOverload_RecordsTheInstanceWinner()
    {
        var analyzer = Analyze("""
            Module M
             Sub Main()
              Dim r As New Regex("a")
              Dim ok = r.IsMatch("x")
             End Sub
            End Module
            """);

        Assert.That(FindingCodes(analyzer), Is.Empty);
        var isMatch = analyzer.NetResolvedMembers.Values.Single(m => m.Name == "IsMatch");
        Assert.That(isMatch.IsStatic, Is.False,
            "r.IsMatch(\"x\") must select the INSTANCE IsMatch(String), not a static overload");
        Assert.That(isMatch.Parameters.Select(p => p.TypeFullName),
            Is.EqualTo(new[] { "System.String" }));
    }

    [Test]
    public void AmbiguousOverload_DrawsBl6018()
    {
        var probe = EmitProbeAssembly("BlnetAmbig", """
            namespace AmbigLib
            {
                public static class AmbigType
                {
                    public static void Boom(int a, long b) { }
                    public static void Boom(long a, int b) { }
                }
            }
            """);
        var resolver = NetTypeResolver.Create(
            NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { probe }));

        var analyzer = Analyze("""
            Using AmbigLib

            Module M
             Sub Main()
              AmbigType.Boom(1, 2)
             End Sub
            End Module
            """, resolverFactory: () => resolver);

        var bl6018 = analyzer.NetDiagnostics.Where(d => d.Code == "BL6018").ToList();
        Assert.That(bl6018, Has.Count.EqualTo(1),
            "Boom(int,long) vs Boom(long,int) with (Integer, Integer) arguments is C#'s CS0121 "
            + "— the one signal that separates ambiguity from absence — and must surface as "
            + "BL6018, never BL6017. Got: "
            + string.Join(" | ", analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.That(bl6018[0].Message, Does.Contain("Boom"));
        Assert.That(bl6018[0].IsWarning, Is.True,
            "SEVERITY DISCIPLINE: everything stays IsWarning until the Task-5 flip.");
    }

    [Test]
    public void NoMatchingOverload_DrawsBl6017_WithTheArgumentTypesInTheMessage()
    {
        var analyzer = Analyze("""
            Module M
             Sub Main()
              Dim s = Convert.ToBase64String("hello")
             End Sub
            End Module
            """);

        var bl6017 = analyzer.NetDiagnostics.Where(d => d.Code == "BL6017").ToList();
        Assert.That(bl6017, Has.Count.EqualTo(1),
            "no ToBase64String overload takes a String — the argument-side half of BL6017. Got: "
            + string.Join(" | ", analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.That(bl6017[0].Message, Does.Contain("System.String"),
            "the message must name the argument types the call presented");
        Assert.That(bl6017[0].Message, Does.Contain("ToBase64String"));
        Assert.That(bl6017[0].IsWarning, Is.True);
    }

    [Test]
    public void UserDefinedClassArgument_DrawsBl6019()
    {
        var analyzer = Analyze("""
            Class Player
             Public Name As String
            End Class

            Module M
             Sub Main()
              Dim p As New Player()
              Dim s = Convert.ToBase64String(p)
             End Sub
            End Module
            """);

        var bl6019 = analyzer.NetDiagnostics.Where(d => d.Code == "BL6019").ToList();
        Assert.That(bl6019, Has.Count.EqualTo(1),
            "§6.5's argument-side rule: a user-defined BasicLang class is never an admissible "
            + ".NET argument. Got: "
            + string.Join(" | ", analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.That(bl6019[0].Message, Does.Contain("Player"));
        Assert.That(bl6019[0].IsWarning, Is.True);
        Assert.That(FindingCodes(analyzer), Does.Not.Contain("BL6017"),
            "the user-class case must not ALSO be misreported as a failed overload match");
    }

    /// <summary>
    /// `Nothing` participates as a null literal (§6.5), but ResolveOverload's grammar has no
    /// spelling for null — and its contract is explicit that a caller that cannot type every
    /// argument must not ask. The call stays name-only recorded with NO manufactured finding.
    /// (Reported to the coordinator as a Task-4 deferral.)
    /// </summary>
    [Test]
    public void NothingArgument_LeavesTheCallNameOnlyRecorded_WithNoFinding()
    {
        var analyzer = Analyze("""
            Module M
             Sub Main()
              Dim s = Convert.ToBase64String(Nothing)
             End Sub
            End Module
            """);

        Assert.That(FindingCodes(analyzer), Is.Empty,
            "a Nothing argument must not manufacture a finding — the analyzer cannot spell "
            + "null for the probe, and a wrong guess would be a spurious BL6017/BL6018");
        Assert.That(analyzer.NetResolvedMembers.Values.Any(m => m.Name == "ToBase64String"),
            Is.True, "the member probe's name-only record must survive for the collector");
    }

    // ====================================================================================
    // BL6024 — .NET call inside a generic BasicLang body (native only)
    // ====================================================================================

    private const string GenericBodyProgram = """
        Module M
         Function F(Of T)(x As T) As String
          Dim txt = File.ReadAllText("a.txt")
          Return "done"
         End Function
         Sub Main()
          Console.WriteLine("done")
         End Sub
        End Module
        """;

    [Test]
    public void NetCallInsideAGenericBody_DrawsBl6024_OnTheNativePath()
    {
        var analyzer = Analyze(GenericBodyProgram, nativeBackend: true);

        var bl6024 = analyzer.NetDiagnostics.Where(d => d.Code == "BL6024").ToList();
        Assert.That(bl6024, Has.Count.EqualTo(1),
            "File.ReadAllText resolved inside generic F(Of T) — BasicLang generics emit real "
            + "C++ templates, so the instantiation set is unknowable at phase 3 (§15.5/§14.13). Got: "
            + string.Join(" | ", analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.That(bl6024[0].Message, Does.Contain("ReadAllText"));
        Assert.That(bl6024[0].IsWarning, Is.True,
            "SEVERITY DISCIPLINE: BL6024 is a warning until the Task-5 flip.");
    }

    [Test]
    public void NetCallInsideAGenericBody_IsSilentOnTheCSharpPath()
    {
        var analyzer = Analyze(GenericBodyProgram, nativeBackend: false);
        Assert.That(FindingCodes(analyzer), Does.Not.Contain("BL6024"),
            "the C# backend compiles generic bodies with .NET calls fine — BL6024 is native-only");
    }

    [Test]
    public void NetCallOutsideAGenericBody_DrawsNoBl6024()
    {
        var analyzer = Analyze("""
            Module M
             Sub Main()
              Dim txt = File.ReadAllText("a.txt")
             End Sub
            End Module
            """);
        Assert.That(FindingCodes(analyzer), Does.Not.Contain("BL6024"));
    }

    // ====================================================================================
    // Evidence-bar replacement (§6.5) — bare names resolve through the ambient set natively
    // ====================================================================================

    [Test]
    public void BareUnclaimedGeneric_ResolvesThroughAmbientNamespaces_NoFinding()
    {
        var analyzer = Analyze("""
            Module M
             Sub Main()
              Dim q As Queue(Of Integer)
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        Assert.That(FindingCodes(analyzer), Is.Empty,
            "bare Queue(Of Integer) must RESOLVE through the ambient System.Collections.Generic "
            + "(§6.5's evidence-bar replacement) — a finding here means the native path still "
            + "requires the System.-rooted spelling. Got: "
            + string.Join(" | ", analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
    }

    [Test]
    public void BareUnresolvableTypeName_DrawsBl6016_OnTheNativePathOnly()
    {
        const string source = """
            Module M
             Sub Main()
              Dim z As ZzqNotARealTypeAnywhere
              Console.WriteLine("done")
             End Sub
            End Module
            """;

        var native = Analyze(source, nativeBackend: true);
        var bl6016 = native.NetDiagnostics.Where(d => d.Code == "BL6016").ToList();
        Assert.That(bl6016, Has.Count.EqualTo(1),
            "on the NATIVE path a bare name no user channel claimed and the closure cannot "
            + "resolve is a real finding (warning here, error at the Task-5 flip). Got: "
            + string.Join(" | ", native.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.That(bl6016[0].Message, Does.Contain("ZzqNotARealTypeAnywhere"));
        Assert.That(bl6016[0].IsWarning, Is.True);

        var csharp = Analyze(source, nativeBackend: false);
        Assert.That(FindingCodes(csharp), Is.Empty,
            "the C# path RETAINS the System.-rooted lexical bar (§6.5): a bare name may "
            + "legitimately live outside the shared-framework closure there");
    }

    [Test]
    public void SystemRootedUnresolvableName_StillDrawsBl6016_OnTheCSharpPath()
    {
        var analyzer = Analyze("""
            Module M
             Sub Main()
              Dim x As System.ThisTypeDoesNotExistAnywhere
              Console.WriteLine("done")
             End Sub
            End Module
            """, nativeBackend: false);

        Assert.That(FindingCodes(analyzer), Does.Contain("BL6016"),
            "the C#-path bar still judges System.-rooted spellings — that half predates Task 4 "
            + "and must survive the bar's relocation into ResolveTypeName's fallback branch");
    }

    [Test]
    public void ConsoleReadKeyResolves_WhileConsoleWriteLineStaysClaimed()
    {
        var analyzer = Analyze("""
            Module M
             Sub Main()
              Console.WriteLine("x")
              Dim k = Console.ReadKey()
             End Sub
            End Module
            """);

        Assert.That(FindingCodes(analyzer), Is.Empty);
        Assert.That(analyzer.NetResolvedMembers.Values.Any(m => m.Name == "ReadKey"), Is.True,
            "Console.ReadKey is UNCLAIMED (§6.5 row (c) is per-call) and must resolve + annotate");
        Assert.That(analyzer.NetResolvedMembers.Values.Any(m => m.Name == "WriteLine"), Is.False,
            "Console.WriteLine is claimed by row (c) (EmitStdLibCall arm) and must NEVER be "
            + "annotated — routing it through the shim would rewrite every existing program");
    }

    // ====================================================================================
    // D-P1 Step 2a — the two-name System.Object allowlist (§14.15)
    // ====================================================================================

    [Test]
    public void Dp1_StreamResolvesToStringAndGetHashCode()
    {
        // System.IO.Stream overrides neither — the spec's clean case.
        var r = SharedResolver.Value;

        var toString = r.ResolveOverload("System.IO.Stream", NetCallForm.Instance, "ToString",
                                         Array.Empty<string>());
        Assert.That(toString.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "D-P1: nullary ToString() is admitted from System.Object for a non-overriding type");
        Assert.That(toString.Member!.DeclaringTypeFullName, Is.EqualTo("System.Object"));
        Assert.That(toString.Member.TypeFullName, Is.EqualTo("System.String"));

        var getHashCode = r.ResolveOverload("System.IO.Stream", NetCallForm.Instance,
                                            "GetHashCode", Array.Empty<string>());
        Assert.That(getHashCode.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved));
        Assert.That(getHashCode.Member!.DeclaringTypeFullName, Is.EqualTo("System.Object"));
        Assert.That(getHashCode.Member.TypeFullName, Is.EqualTo("System.Int32"));

        Assert.That(r.GetMembers("System.IO.Stream").Select(m => m.Name),
            Does.Contain("GetHashCode"),
            "GetMembers and ResolveOverload must agree — the allowlist lives in "
            + "CandidateMembers, the single seam both consume");
    }

    [Test]
    public void Dp1_GetTypeStaysNoMatch()
    {
        Assert.That(SharedResolver.Value.ResolveOverload(
                "System.IO.Stream", NetCallForm.Instance, "GetType", Array.Empty<string>()).Outcome,
            Is.EqualTo(NetOverloadOutcome.NoMatch),
            "D-P1 is a TWO-name allowlist. GetType() is the reflection root and stays excluded "
            + "— this is what keeps the allowlist from becoming 'any marshalable nullary Object "
            + "member'.");
    }

    [Test]
    public void Dp1_EqualsObjectStaysNoMatch()
    {
        Assert.That(SharedResolver.Value.ResolveOverload(
                "System.IO.Stream", NetCallForm.Instance, "Equals",
                new[] { "System.Object" }).Outcome,
            Is.EqualTo(NetOverloadOutcome.NoMatch),
            "Equals(Object) stays excluded — §8.3: Object is permanently Rejected, so the "
            + "parameter is unmarshalable regardless of the allowlist.");
    }

    [Test]
    public void Dp1_StringBuilderToStringResolvesToTheOverride()
    {
        var r = SharedResolver.Value;

        var overridden = r.ResolveOverload("System.Text.StringBuilder", NetCallForm.Instance,
                                           "ToString", Array.Empty<string>());
        Assert.That(overridden.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved));
        Assert.That(overridden.Member!.DeclaringTypeFullName,
            Is.EqualTo("System.Text.StringBuilder"),
            "an OVERRIDE must win over the allowlist entry — the signature-identity collapse "
            + "keeps the most-derived declaration and drops System.Object's");

        var nullaryToStrings = r.GetMembers("System.Text.StringBuilder")
            .Where(m => m.Name == "ToString" && m.Parameters.Count == 0).ToList();
        Assert.That(nullaryToStrings, Has.Count.EqualTo(1),
            "exactly ONE nullary ToString — the allowlist entry must collapse into the override");
        Assert.That(nullaryToStrings[0].DeclaringTypeFullName,
            Is.EqualTo("System.Text.StringBuilder"));
    }

    /// <summary>
    /// The ObjectMemberNames-lift half of Step 2a: without it the allowlisted members resolve in
    /// the RESOLVER but the analyzer's early-out suppresses both the annotation and any finding,
    /// so nothing ever reaches the surface collector.
    /// </summary>
    [Test]
    public void Dp1_ProbeAnnotatesToStringOnAResolvedNonOverridingReceiver()
    {
        var analyzer = Analyze("""
            Module M
             Sub Main()
              Dim st As Stream
              Dim s = st.ToString()
             End Sub
            End Module
            """);

        Assert.That(FindingCodes(analyzer), Is.Empty,
            "st.ToString() is a valid call and must draw nothing. Got: "
            + string.Join(" | ", analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
        var toString = analyzer.NetResolvedMembers.Values
            .SingleOrDefault(m => m.Name == "ToString");
        Assert.That(toString, Is.Not.Null,
            "the D-P1 lift must let ToString through the ObjectMemberNames early-out so it is "
            + "ANNOTATED (and later surfaced) — resolution alone is not enough");
        Assert.That(toString!.DeclaringTypeFullName, Is.EqualTo("System.Object"));
    }

    [Test]
    public void Dp1_GetTypeOnAResolvedReceiverStaysSilentAndUnannotated()
    {
        var analyzer = Analyze("""
            Module M
             Sub Main()
              Dim st As Stream
              Dim t = st.GetType()
             End Sub
            End Module
            """);

        Assert.That(FindingCodes(analyzer), Is.Empty,
            "GetType() keeps its early-out: absent from the surface BY DESIGN, so its absence "
            + "must not be reported");
        Assert.That(analyzer.NetResolvedMembers.Values.Any(m => m.Name == "GetType"), Is.False);
    }

    // ====================================================================================
    // §11.1 ladder-trigger completion — a RESOLVED exception type gets its arm
    // ====================================================================================

    [Test]
    public void ResolvedExceptionTypeOutsideThe12Names_GetsItsOwnNetExceptionLadderArm()
    {
        var cpp = CompileToCppWithResolver("""
            Module M
             Sub Main()
              Try
               Console.WriteLine("in")
              Catch e As FileNotFoundException
               Console.WriteLine(e.Message)
              Catch ex As Exception
               Console.WriteLine("other")
              End Try
             End Sub
            End Module
            """);

        var fnfArm = cpp.IndexOf("__nex.Matches(\"System.IO.FileNotFoundException\")",
                                 StringComparison.Ordinal);
        var exceptionArm = cpp.IndexOf("__nex.Matches(\"System.Exception\")",
                                       StringComparison.Ordinal);

        Assert.That(fnfArm, Is.GreaterThanOrEqualTo(0),
            "a clause type OUTSIDE the 12-name set that RESOLVES as a .NET exception must get "
            + "its own ladder arm with the resolver-supplied FQ name — without the carriage it "
            + "silently binds to the later Exception clause (§11.1's second trigger half):\n"
            + cpp);
        Assert.That(exceptionArm, Is.GreaterThanOrEqualTo(0),
            "guard: the Exception clause keeps its arm:\n" + cpp);
        Assert.That(fnfArm, Is.LessThan(exceptionArm),
            "arms must follow clause SOURCE ORDER — first matching clause wins:\n" + cpp);

        // One leading NetException handler for the whole Try, not one per clause.
        var handlerCount = 0;
        for (var i = cpp.IndexOf("catch (const BasicLang::NetException&", StringComparison.Ordinal);
             i >= 0;
             i = cpp.IndexOf("catch (const BasicLang::NetException&", i + 1, StringComparison.Ordinal))
        {
            handlerCount++;
        }
        Assert.That(handlerCount, Is.EqualTo(1), "per-Try, not per-clause:\n" + cpp);
    }

    [Test]
    public void UserDefinedCatchType_GetsNoLadderArm()
    {
        var cpp = CompileToCppWithResolver("""
            Class MyError
             Public Msg As String
            End Class

            Module M
             Sub Main()
              Try
               Console.WriteLine("in")
              Catch e As MyError
               Console.WriteLine("mine")
              Catch ex As Exception
               Console.WriteLine("other")
              End Try
             End Sub
            End Module
            """);

        Assert.That(cpp, Does.Not.Contain("__nex.Matches(\"MyError\""),
            "a BasicLang exception type can never arrive as a managed NetException — no arm");
        Assert.That(cpp, Does.Contain("__nex.Matches(\"System.Exception\")"),
            "guard: the Exception clause still arms the ladder");
    }

    // ====================================================================================
    // D-P2 (§15.11) — net9+ reference draws BL6021 naming the TFM
    // ====================================================================================

    private static string TfmAttributedSource(string tfm) => $$"""
        [assembly: System.Runtime.Versioning.TargetFramework("{{tfm}}",
                   FrameworkDisplayName = "probe")]
        namespace TfmLib { public static class TfmType { public static int Answer() => 42; } }
        """;

    [Test]
    public void Net9AttributedReference_DrawsBl6021_NamingTheTfmAndThePinnedShim()
    {
        var probe = EmitProbeAssembly("BlnetNine", TfmAttributedSource(".NETCoreApp,Version=v9.0"));

        var project = new ProjectFile { Backend = "cpp" };
        project.AssemblyReferences.Add(new AssemblyReference
        { Name = "BlnetNine", HintPath = Path.GetFileName(probe) });

        var closure = NetReferenceResolver.Resolve(
            project, Path.Combine(_dir, "probe.blproj"));

        var tfmFindings = closure.Diagnostics
            .Where(d => d.Code == "BL6021" && d.Message.Contains("newer than")).ToList();
        Assert.That(tfmFindings, Has.Count.EqualTo(1),
            "D-P2: a resolved reference targeting .NETCoreApp ≥ 9.0 must draw BL6021 — the "
            + "pinned net8.0 shim cannot reference it. Got: "
            + string.Join(" | ", closure.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.That(tfmFindings[0].Message, Does.Contain(".NETCoreApp,Version=v9.0"),
            "the message must name the offending TFM");
        Assert.That(tfmFindings[0].Message, Does.Contain("net8.0"),
            "…and the shim's pinned TFM, so the fix is actionable");
        Assert.That(tfmFindings[0].IsWarning, Is.True,
            "SEVERITY DISCIPLINE: warning until the Task-5 flip");
        Assert.That(closure.AssemblyPaths, Has.Count.EqualTo(1),
            "guard: the reference RESOLVED — D-P2 is about a resolved-but-too-new assembly");
    }

    [Test]
    public void Net8AttributedReference_DrawsNoTfmFinding()
    {
        var probe = EmitProbeAssembly("BlnetEight", TfmAttributedSource(".NETCoreApp,Version=v8.0"));

        var project = new ProjectFile { Backend = "cpp" };
        project.AssemblyReferences.Add(new AssemblyReference
        { Name = "BlnetEight", HintPath = Path.GetFileName(probe) });

        var closure = NetReferenceResolver.Resolve(
            project, Path.Combine(_dir, "probe.blproj"));

        Assert.That(closure.Diagnostics.Where(d => d.Message.Contains("newer than")), Is.Empty,
            "net8.0 is exactly the shim's TFM — no finding");
        Assert.That(closure.AssemblyPaths, Has.Count.EqualTo(1), "guard: it resolved");
    }
}
