using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.Net;
using BasicLang.Compiler.ProjectSystem;
using BasicLang.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Plan Task 13 — <see cref="CppProjectBuilder.EmitCore"/>'s phase model, the §9.5 merge of
/// <c>GenerateSplit</c>'s output with <see cref="NetProxyEmitter"/>'s, and §10.3's cancellation.
///
/// <para><b>This task's success criterion is that NOTHING CHANGES for an existing project.</b>
/// <c>EmitCore</c> is the ONE native build path, shared verbatim by the CLI and the IDE, and it
/// also runs toolchain-free on the IntelliSense path. The 108 pre-existing C++ fixtures
/// (<c>CppBackendTests</c> / <c>CppCollectionTests</c> / <c>CppBclEndToEndTests</c>) are the
/// coarse proof; this fixture is the fine one, at the exact seam that moved.</para>
///
/// <para><b>How the "same file set" oracle avoids being a tautology.</b>
/// <see cref="EmptySurfaceWithBasSources_WritesExactlyTheKnownFileSet"/> compares against a
/// HAND-WRITTEN literal list derived from <c>GenerateSplit</c>'s documented naming rule
/// (<c>&lt;Project&gt;.g.h</c>, <c>&lt;Module&gt;.g.cpp</c>, <c>&lt;Module&gt;.g.h</c>,
/// <c>&lt;Project&gt;.main.g.cpp</c>, <c>BasicLangRuntime.g.h</c>) — not against whatever the
/// builder happened to produce. If the merge writes a proxy artifact for an empty surface, an
/// extra name appears and it goes red; if the merge drops split's files, a name disappears.
/// <see cref="EmptySurfaceWithBasSources_WritesByteIdenticalContent"/> then adds CONTENT, with
/// <c>GenerateSplit</c> itself as the oracle — driven directly, not through <c>EmitCore</c>.
/// <c>GenerateSplit</c> is upstream of everything this task touched, so "what EmitCore wrote"
/// and "what GenerateSplit produced" are genuinely two different things to compare.</para>
///
/// <para><b>Mutation-tested during Task 13.</b> Making phase 3 report a non-empty surface for
/// every project — <c>surfaceOverride ?? new NetSurface([], ["System.String"])</c> in place of
/// <c>NetSurface.Empty</c>, i.e. exactly the regression that would emit .NET artifacts into a
/// project with no .NET — turned FOUR of these red:
/// <see cref="EmptySurfaceWithBasSources_WritesExactlyTheKnownFileSet"/>,
/// <see cref="EmptySurfaceWithBasSources_PassesOnlySplitTusToTheCompiler"/>,
/// <see cref="LibraryOutputWithEmptySurface_BuildsExactlyAsBefore"/> and
/// <see cref="UncancelledToken_ChangesNothing"/>. A file-set test that can pass under that
/// mutation is worthless.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class NetBuildPipelineTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "blnet-pipeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch (IOException) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) { Thread.Sleep(200); }
        }
    }

    // ---- The probe project --------------------------------------------------------------

    private const string ProjectName = "FileSetProbe";

    /// <summary>
    /// TWO modules, so the per-module <c>.g.cpp</c>/<c>.g.h</c> pairs are actually exercised —
    /// a single-module project would leave the module loop with one iteration and hide an
    /// ordering bug in the merged write.
    /// </summary>
    private const string LogicSource = """
        Module Logic
         Function Twice(n As Integer) As Integer
          Return n + n
         End Function
        End Module
        """;

    /// <summary>
    /// File name and module name are kept identical on purpose. The per-module shim header is
    /// named from the UNIT's module roster, so a <c>Program.bas</c> declaring
    /// <c>Module Program</c> makes the expected file names below readable instead of a puzzle.
    /// </summary>
    private const string ProgramSource = """
        Module Program
         Sub Main()
          Console.WriteLine(Twice(21))
         End Sub
        End Module
        """;

    /// <summary>
    /// <b>The independent oracle for the file SET.</b> Hand-written from <c>GenerateSplit</c>'s
    /// naming rule (<c>CppCodeGenerator.Split.cs</c>), NOT captured from a run:
    /// <list type="bullet">
    /// <item><c>&lt;Project&gt;.g.h</c> — the aggregate header.</item>
    /// <item><c>&lt;Module&gt;.g.h</c> shim header for every name in the UNIT module roster,
    /// which is unit-derived — hence the fixture's insistence that each file's name match the
    /// module it declares.</item>
    /// <item><c>&lt;Module&gt;.g.cpp</c> per roster name whose definition bucket is non-empty.
    /// A function whose <c>IRFunction.ModuleName</c> is NOT in the roster falls into
    /// <c>&lt;Project&gt;.__shared.g.cpp</c> instead — which is why that name is absent here and
    /// why a mismatched file/module pair would change the set. (Deriving this list by hand is
    /// what surfaced that rule; a list captured from a run would have agreed with any behavior
    /// at all.)</item>
    /// <item><c>&lt;Project&gt;.main.g.cpp</c> — this project is an Exe with exactly one
    /// standalone <c>Sub Main</c>, so <c>emitMain</c> is true.</item>
    /// <item><c>BasicLangRuntime.g.h</c>.</item>
    /// </list>
    /// Six names are conspicuously ABSENT — <see cref="NetProxyEmitter"/>'s — and that absence
    /// is the whole point.
    /// </summary>
    private static readonly string[] ExpectedGeneratedFileNames =
    {
        "BasicLangRuntime.g.h",
        "FileSetProbe.g.h",
        "FileSetProbe.main.g.cpp",
        "Logic.g.cpp",
        "Logic.g.h",
        "Program.g.cpp",
        "Program.g.h",
    };

    /// <summary>
    /// The subset of <see cref="ExpectedGeneratedFileNames"/> that is handed to the COMPILER
    /// rather than merely included — <c>GenerateSplit</c>'s <c>TranslationUnitFileNames</c>,
    /// derived by the same hand rule.
    /// </summary>
    private static readonly string[] ExpectedSplitTuNames =
    {
        "FileSetProbe.main.g.cpp",
        "Logic.g.cpp",
        "Program.g.cpp",
    };

    private static string Blproj(string outputType) => $"""
        <BasicLangProject Version="1.0">
          <PropertyGroup>
            <ProjectName>{ProjectName}</ProjectName>
            <OutputType>{outputType}</OutputType>
            <TargetBackend>Cpp</TargetBackend>
          </PropertyGroup>
        </BasicLangProject>
        """;

    /// <summary>
    /// A resolvable toolchain that is never RUN. <c>EmitCore</c> reads only
    /// <c>Kind</c>/<c>DriverName</c>/<c>DisplayName</c> off it and stops at the
    /// compile-database write, so a non-existent path is enough to get past the BL6005 gate and
    /// reach <c>outcome.Request</c> — which is where the source list and include path live.
    /// This keeps every test here toolchain-free (no <c>[Category("Integration")]</c>) while
    /// still exercising the BUILD path rather than the IntelliSense one.
    /// </summary>
    private static CppToolchain FakeToolchain() =>
        CppToolchain.FromExplicit("llvm", Path.Combine(Path.GetTempPath(), "no-such-clang++.exe"));

    private string WriteProject(IReadOnlyDictionary<string, string> files, string outputType = "Exe")
    {
        foreach (var file in files)
        {
            var full = Path.Combine(_dir, file.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, file.Value);
        }
        var projectPath = Path.Combine(_dir, ProjectName + ".blproj");
        File.WriteAllText(projectPath, Blproj(outputType));
        return projectPath;
    }

    private (CppProjectBuildResult Result, CppEmitOutcome Outcome) Emit(
        IReadOnlyDictionary<string, string> files, string outputType = "Exe",
        NetSurface? surfaceOverride = null,
        CancellationToken cancellationToken = default, bool forIntelliSense = false)
    {
        var projectPath = WriteProject(files, outputType);
        var result = new CppProjectBuildResult();
        var outcome = CppProjectBuilder.EmitCore(
            ProjectFile.Load(projectPath), "Release", result,
            resolveToolchain: FakeToolchain,
            forIntelliSense: forIntelliSense,
            surfaceOverride: surfaceOverride,
            cancellationToken: cancellationToken,
            // Phase 5 OFF for the whole fixture (P2a-2 Task 7b). Every non-empty surface here is
            // hand-built and names types like "MyLib.Sink" that no assembly declares, so a real
            // AOT publish would spend ~27 s failing to compile a shim for reasons that say nothing
            // about the §9.5 merge under test. Phase 5's own proofs live in NetShimPipelineTests,
            // which uses real programs under [Category("Integration")].
            publishShim: false);
        return (result, outcome);
    }

    private static Dictionary<string, string> TwoModuleProject() => new()
    {
        ["Logic.bas"] = LogicSource,
        ["Program.bas"] = ProgramSource,
    };

    private string ObjGenDir => Path.Combine(_dir, "obj", "gen");

    private IReadOnlyList<string> GeneratedFileNames() =>
        Directory.Exists(ObjGenDir)
            ? Directory.EnumerateFiles(ObjGenDir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList()
            : Array.Empty<string>();

    /// <summary>
    /// Normalizes line endings on BOTH sides of every text comparison — <c>core.autocrlf</c> is
    /// true in this repo, so nothing here may compare raw bytes.
    /// </summary>
    private static string N(string s) => s.Replace("\r\n", "\n");

    private static void AssertNoErrors(CppProjectBuildResult result, string label)
    {
        var errors = result.Diagnostics.Where(d => !d.IsWarning)
            .Select(d => d.Code + ": " + d.Message).ToList();
        Assert.That(errors, Is.Empty,
            $"{label}: the probe project no longer emits cleanly, so nothing below means anything. "
            + "Fix the fixture's BasicLang source or the compiler — not the assertions: "
            + string.Join(" | ", errors));
    }

    // =====================================================================================
    // 1. .bas sources + EMPTY surface — the case every existing project is in.
    // =====================================================================================

    [Test]
    public void EmptySurfaceWithBasSources_WritesExactlyTheKnownFileSet()
    {
        var (result, outcome) = Emit(TwoModuleProject());

        AssertNoErrors(result, "empty surface + .bas sources");
        Assert.That(outcome.Completed, Is.True, "EmitCore did not reach the compile-database write.");

        Assert.That(GeneratedFileNames(), Is.EqualTo(ExpectedGeneratedFileNames.OrderBy(n => n, StringComparer.Ordinal)),
            "obj/gen no longer holds exactly the file set GenerateSplit's naming rule produces. "
            + "If EXTRA names appeared and they are NetProxyEmitter's (blnet.h, blnet_runtime.hpp, "
            + "blnet_marshal.hpp, blnet_bindings.g.hpp, blnet_proxies.g.hpp, blnet_startup.g.cpp), "
            + "the §9.5 merge in "
            + "CppProjectBuilder.EmitCore has lost its surface gate and is emitting .NET artifacts "
            + "for a project with no .NET at all — fix EmitCore, not this list. If names are "
            + "MISSING, the merged write dropped split.Files. If the names merely CHANGED, "
            + "CppCodeGenerator.Split.cs's naming rule changed and this literal list is what must "
            + "be updated.");
    }

    [Test]
    public void EmptySurfaceWithBasSources_WritesByteIdenticalContent()
    {
        var (result, _) = Emit(TwoModuleProject());
        AssertNoErrors(result, "empty surface + .bas sources");

        // The oracle: GenerateSplit driven DIRECTLY, with the inputs EmitCore derives for a
        // Release build of this project (Release => DebugSymbols false => no #line directives,
        // ProjectFile.cs's default configuration set). GenerateSplit is upstream of everything
        // Task 13 touched, so this compares two genuinely different things.
        var expected = ExpectedSplitFiles();

        Assert.Multiple(() =>
        {
            foreach (var kv in expected)
            {
                var path = Path.Combine(ObjGenDir, kv.Key);
                Assert.That(File.Exists(path), Is.True, $"obj/gen is missing {kv.Key}.");
                Assert.That(N(File.ReadAllText(path)), Is.EqualTo(N(kv.Value)),
                    $"The bytes EmitCore wrote for {kv.Key} are not the bytes GenerateSplit "
                    + "produced. The merged write in CppProjectBuilder.EmitCore is transforming or "
                    + "overwriting split output — nothing in Task 13 may change generated C++.");
            }
        });
    }

    /// <summary>
    /// Re-derives <c>GenerateSplit</c>'s output the way <c>EmitCore</c> does: sanitized project
    /// name (identity for "FileSetProbe"), per-unit IRs, <c>emitMain: true</c> (Exe with exactly
    /// one standalone <c>Sub Main</c>), and no <c>#line</c> directives (Release).
    /// <c>NetResolverFactory</c> is deliberately NOT set — it is lazy and produces DIAGNOSTICS
    /// only, never IR, so it cannot influence generated text.
    /// </summary>
    private IReadOnlyDictionary<string, string> ExpectedSplitFiles()
    {
        var sources = new[] { Path.Combine(_dir, "Logic.bas"), Path.Combine(_dir, "Program.bas") };
        var compiler = new BasicCompiler(new CompilerOptions { TargetBackend = "cpp" });
        var compilation = compiler.CompileProjectFiles(sources);
        Assert.That(compilation.Success, Is.True,
            "The oracle's own compile failed, so it is not an oracle. "
            + string.Join(" | ", compilation.AllErrors.Select(e => e.Message)));

        var unitIRs = compilation.Units.Select(u => u.IR).Where(ir => ir != null).ToList();
        return new CppCodeGenerator(new CppCodeGenOptions { EmitLineDirectives = false })
            .GenerateSplit(compilation.CombinedIR, ProjectName, unitIRs, emitMain: true)
            .Files;
    }

    /// <summary>
    /// The include path and the compile inputs, for the same empty-surface case. A merge that
    /// wrote the right files but handed the compiler the wrong list would pass both tests above.
    /// </summary>
    [Test]
    public void EmptySurfaceWithBasSources_PassesOnlySplitTusToTheCompiler()
    {
        var (result, outcome) = Emit(TwoModuleProject());
        AssertNoErrors(result, "empty surface + .bas sources");

        var tuNames = outcome.Request.SourceFiles.Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(tuNames, Is.EqualTo(ExpectedSplitTuNames.OrderBy(n => n, StringComparer.Ordinal)),
                "The compile inputs for a project with no .NET changed. blnet_startup.g.cpp "
                + "appearing here means the generatedTus union lost its surface gate in "
                + "CppProjectBuilder.EmitCore.");
            Assert.That(outcome.Request.IncludeDirs, Does.Contain(ObjGenDir),
                "obj/gen dropped off the include path, so user C++ can no longer #include the "
                + "generated shim headers. The include gate moved from blSources.Count > 0 to the "
                + "merged artifact set — the two must agree whenever the surface is empty.");
        });
    }

    // =====================================================================================
    // 2. ZERO .bas sources + NON-EMPTY surface — the pure-C++ consumer of §9.5.
    // =====================================================================================

    private static Dictionary<string, string> PureCppProject() => new()
    {
        ["main.cpp"] = "int main() { return 0; }\n",
    };

    [Test]
    public void NonEmptySurfaceWithNoBasSources_EmitsTheProxyArtifactsAndLinksTheStartupTu()
    {
        var (result, outcome) = Emit(PureCppProject(), surfaceOverride: NetProxyEmitterTests.SixShapeSurface());

        AssertNoErrors(result, "non-empty surface + zero .bas sources");
        Assert.That(outcome.Completed, Is.True, "EmitCore did not reach the compile-database write.");

        var startupPath = Path.Combine(ObjGenDir, NetProxyEmitter.StartupFileName);

        Assert.Multiple(() =>
        {
            // The artifact set is NetProxyEmitter's six and ONLY those: with no .bas files there
            // is no split at all, so anything else here came from somewhere it should not have.
            Assert.That(GeneratedFileNames(), Is.EqualTo(new[]
                {
                    NetProxyEmitter.BindingsFileName,
                    NetProxyEmitter.ProxiesFileName,
                    NetProxyEmitter.StartupFileName,
                    NetProxyEmitter.ContractHeaderFileName,
                    NetProxyEmitter.RuntimeHeaderFileName,
                    NetProxyEmitter.MarshalHeaderFileName,   // §6.4 conversion pairs (P2a-2 Task 6)
                }.OrderBy(n => n, StringComparer.Ordinal)),
                "A pure-C++ project with a .NET surface did not get exactly NetProxyEmitter's "
                + "artifact set in obj/gen. If it got NOTHING, the obj/gen write is still gated on "
                + "blSources.Count > 0 rather than on the merged set (§9.5).");

            Assert.That(outcome.Request.SourceFiles, Does.Contain(startupPath),
                "blnet_startup.g.cpp is emitted but never compiled or linked — the generatedTus "
                + "union in CppProjectBuilder.EmitCore is not picking up "
                + "NetProxyEmitter.TranslationUnitFileNames. Without it every proxy slot stays "
                + "null and g_net is never defined.");

            Assert.That(outcome.Request.IncludeDirs, Does.Contain(ObjGenDir),
                "obj/gen is not on the include path, so the generated blnet_startup.g.cpp cannot "
                + "resolve its own #include \"blnet.h\". The include gate must key off the merged "
                + "artifact set, not off .bas sources.");

            // The user's own TU is still there — the merge ADDS, it does not replace.
            Assert.That(outcome.Request.SourceFiles.Select(Path.GetFileName), Does.Contain("main.cpp"));
        });
    }

    /// <summary>
    /// The <c>split</c>-null hazard §9.5 names explicitly: relaxing the gate to
    /// <c>surface.IsNonEmpty || blSources.Count > 0</c> null-dereferences <c>split.Files</c>.
    /// The test above already covers the happy path; this one states the failure mode so a
    /// future reader knows why the null check exists rather than "tidying" it away.
    /// </summary>
    [Test]
    public void NonEmptySurfaceWithNoBasSources_DoesNotThrow()
    {
        Assert.DoesNotThrow(
            () => { Emit(PureCppProject(), surfaceOverride: NetProxyEmitterTests.OneMemberSurface()); },
            "EmitCore threw on a project with a .NET surface and no .bas files. `split` is null "
            + "there, so every use of split.Files / split.TranslationUnitFileNames must sit under "
            + "its own null check — the merge is not a widened `if` (spec §9.5).");
    }

    /// <summary>
    /// P2a-2 Task 7a, C1 REGRESSION — <b>the entry point the emit-level fixtures cannot
    /// see.</b> They build IR via <c>new IRBuilder(analyzer)</c> with no
    /// <c>ConfigureModuleSystem</c>, so <c>CurrentUnit</c> is null and
    /// <c>IsKnownNetStaticType</c>'s imported-namespace heuristic is DEAD there. On this path
    /// (and the CLI's) <c>_currentUnit</c> is populated, that heuristic answers TRUE for any
    /// PascalCase identifier once the unit has a .NET <c>Using</c>, and the old routing
    /// condition let it bypass the local/parameter guard — so a plain instance call on a
    /// PascalCase-named local was routed to the FUSED STATIC arm with its receiver discarded.
    /// The lowering then met an instance descriptor with no receiver and failed the build,
    /// telling the user a valid program was a compiler-internal fault.
    ///
    /// <para>CLAUDE.md's "test BOTH entry points" rule, in one test: the program below must
    /// BUILD, and its emitted C++ must pass the receiver handle to the instance proxy.</para>
    /// </summary>
    [Test]
    public void InstanceCallOnAPascalCaseLocal_UnderANetUsing_RoutesThroughTheReceiver()
    {
        var files = new Dictionary<string, string>
        {
            ["Program.bas"] = """
                Using System.Text.RegularExpressions

                Module M
                 Sub Main()
                  Dim Rx As New Regex("a+")
                  Dim ok = Rx.IsMatch("aaa")
                  Console.WriteLine(ok)
                 End Sub
                End Module
                """,
        };

        var (result, _) = Emit(files);

        AssertNoErrors(result, "instance call on a PascalCase local under a .NET Using");

        // Scan every emitted file rather than guessing a name: the split's naming rule keys
        // off the MODULE name ("M"), not the .bas file name.
        var generated = N(string.Join("\n",
            Directory.EnumerateFiles(ObjGenDir).OrderBy(p => p, StringComparer.Ordinal)
                     .Select(File.ReadAllText)));
        Assert.That(generated, Does.Match(@"BasicLang::net::bl_net_\w*Regex_IsMatch\w*\(Rx, ""aaa""\)"),
            "the instance call must pass its RECEIVER to the proxy. Routing it to the fused "
            + "static arm discards the receiver expression — which is how a valid program came "
            + "to fail with a 'compiler-internal' message. FIX IRBuilder's static-vs-instance "
            + "routing, not this test — and look at the DESCRIPTOR CROSS-CHECK first (the "
            + "`!netShape.Member.IsStatic` arm): `_locals` is vestigial, so the isStaticCall "
            + "name-shape guard covers PARAMETERS only and a Dim'd local like `Rx` is caught "
            + "by the cross-check alone:\n" + generated);
    }

    /// <summary>
    /// P2a-2 Task 7a: <c>NetProxyEmitter</c>'s <c>NotSupportedException</c> (a ByRef
    /// handle/String parameter — §8.3 pins ByRef slots only for by-value scalars) became
    /// REACHABLE once real surfaces exist, and the §11.4 contract is a BL6019 diagnostic,
    /// never a crash. Reaching it takes a declared-surface member the §7.2 omission filter
    /// admits (the filter judges parameter TYPES, and String is §8.3-marshalable — the
    /// ref-ness is what the EMITTER refuses), which is exactly what this hand-fed surface is.
    /// </summary>
    [Test]
    public void ByRefStringSurfaceMember_MapsTheEmitterThrowToBl6019()
    {
        var byRefString = new BasicLang.Net.NetMemberDescriptor(
            "TryFrob", "ProbeLib.Widget", BasicLang.Net.NetMemberCategory.Method,
            isStatic: true, arity: 0, "System.Boolean",
            new List<BasicLang.Net.NetParameterDescriptor>
            {
                new(BasicLang.Net.NetRefKind.Ref, "System.String"),
            });
        var surface = new NetSurface(new[] { byRefString }, new[] { "ProbeLib.Widget" });

        CppProjectBuildResult result = null!;
        Assert.DoesNotThrow(
            () => { (result, _) = Emit(TwoModuleProject(), surfaceOverride: surface); },
            "the emitter's NotSupportedException must be MAPPED at the EmitCore call site, "
            + "never escape as a crash (§11.4)");

        var bl6019 = result.Diagnostics.Where(d => d.Code == "BL6019" && !d.IsWarning).ToList();
        Assert.That(bl6019, Has.Count.EqualTo(1),
            "a ByRef String surface member must surface as one BL6019 build error. Got: "
            + string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.That(bl6019[0].Message, Does.Contain("ByRef"),
            "the message must carry the emitter's §8.3 explanation so the fix is actionable");
        Assert.That(bl6019[0].Message, Does.Contain("TryFrob"),
            "…and name the offending member");
    }

    /// <summary>
    /// Both producers at once: .bas sources AND a surface. The merged set is the union, and
    /// neither producer's names are lost.
    /// </summary>
    [Test]
    public void NonEmptySurfaceWithBasSources_MergesBothProducers()
    {
        var (result, outcome) = Emit(TwoModuleProject(),
            surfaceOverride: NetProxyEmitterTests.OneMemberSurface());

        AssertNoErrors(result, "non-empty surface + .bas sources");

        var expected = ExpectedGeneratedFileNames
            .Concat(new[]
            {
                NetProxyEmitter.ContractHeaderFileName,
                NetProxyEmitter.RuntimeHeaderFileName,
                NetProxyEmitter.MarshalHeaderFileName,   // §6.4 conversion pairs (P2a-2 Task 6)
                NetProxyEmitter.BindingsFileName,
                NetProxyEmitter.ProxiesFileName,
                NetProxyEmitter.StartupFileName,
            })
            .OrderBy(n => n, StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(GeneratedFileNames(), Is.EqualTo(expected),
                "obj/gen is not the UNION of GenerateSplit's file set and NetProxyEmitter's. One "
                + "producer is gating the other in CppProjectBuilder.EmitCore (§9.5).");
            Assert.That(outcome.Request.SourceFiles.Select(Path.GetFileName),
                Is.SupersetOf(ExpectedSplitTuNames.Append(NetProxyEmitter.StartupFileName)),
                "The compile inputs are not the union of both producers' translation units.");
        });
    }

    // =====================================================================================
    // 3. BL6025 — a library output cannot use .NET (§9.5, §14.12).
    // =====================================================================================

    /// <summary>
    /// A Library with a NON-empty surface is rejected. The condition is <c>!isExe</c>, not
    /// <c>!emitMain</c>: <c>emitMain</c> is <c>isExe &amp;&amp; basicLangMainCount == 1</c>
    /// (verified in <c>CppProjectBuilder.EmitCore</c>), whose false case also covers an
    /// executable whose entry point is hand-written C++ — a supported shape,
    /// <see cref="ExeWithCppMainAndNonEmptySurface_IsNotRejected"/>.
    /// </summary>
    [Test]
    public void LibraryOutputWithNonEmptySurface_IsRejectedWithBl6025()
    {
        var (result, outcome) = Emit(
            new Dictionary<string, string> { ["Logic.bas"] = LogicSource }, outputType: "Library",
            surfaceOverride: NetProxyEmitterTests.OneMemberSurface());

        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics.Where(d => !d.IsWarning).Select(d => d.Code),
                Does.Contain("BL6025"),
                "A Library output with a non-empty .NET surface was not rejected. §14.12 ships "
                + "this as a known limitation: the generated startup object cannot be guaranteed "
                + "to survive being pulled from a static archive.");
            Assert.That(outcome.Completed, Is.False,
                "BL6025 fired but EmitCore carried on regardless.");
        });
    }

    /// <summary>
    /// <b>The inertness half, and the one that matters today.</b> Every project in the repo has
    /// an empty surface, so a Library must build exactly as it did before BL6025 existed.
    /// </summary>
    [Test]
    public void LibraryOutputWithEmptySurface_BuildsExactlyAsBefore()
    {
        var (result, outcome) = Emit(
            new Dictionary<string, string> { ["Logic.bas"] = LogicSource }, outputType: "Library");

        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics.Select(d => d.Code), Does.Not.Contain("BL6025"),
                "BL6025 fired for a library with an EMPTY surface. It must gate on "
                + "surface.IsNonEmpty — otherwise every existing class library in the repo stops "
                + "building.");
            Assert.That(outcome.Completed, Is.True,
                "A Library with no .NET no longer emits. Task 13 must be inert for it.");
            Assert.That(GeneratedFileNames(), Does.Contain("BasicLangRuntime.g.h"),
                "A Library with no .NET got no generated C++ at all.");
        });
    }

    /// <summary>
    /// An EXE whose only entry point is a hand-written C++ <c>main()</c> has
    /// <c>emitMain == false</c> too. §9.5 supports it — the static-initializer object in
    /// <c>blnet_startup.g.cpp</c> covers both executable shapes — so gating BL6025 on
    /// <c>emitMain</c> instead of <c>isExe</c> would wrongly reject it.
    /// </summary>
    [Test]
    public void ExeWithCppMainAndNonEmptySurface_IsNotRejected()
    {
        var (result, outcome) = Emit(PureCppProject(),
            surfaceOverride: NetProxyEmitterTests.OneMemberSurface());

        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics.Select(d => d.Code), Does.Not.Contain("BL6025"),
                "BL6025 rejected an EXECUTABLE with a user-written C++ main(). The rule is "
                + "!isExe, not !emitMain — emitMain is false for this shape too (§9.5).");
            Assert.That(outcome.Completed, Is.True);
        });
    }

    /// <summary>
    /// BL6025 is bypassed for IntelliSense, alongside every other BUILD-rule gate: enforcing a
    /// link-time rule in the editor would cost the user every generated header, and with them
    /// every completion.
    /// </summary>
    [Test]
    public void Bl6025_IsBypassedForIntelliSense()
    {
        var (result, outcome) = Emit(
            new Dictionary<string, string> { ["Logic.bas"] = LogicSource }, outputType: "Library",
            surfaceOverride: NetProxyEmitterTests.OneMemberSurface(), forIntelliSense: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics.Select(d => d.Code), Does.Not.Contain("BL6025"),
                "BL6025 reached the IntelliSense path. It belongs with BL6007 / BL6005 / the "
                + "entry-point rule in EmitCore's forIntelliSense bypass list.");
            Assert.That(outcome.Completed, Is.True,
                "IntelliSense emission failed for a library that uses .NET, so clangd gets no "
                + "headers at all.");
        });
    }

    // =====================================================================================
    // 4. Cancellation (§10.3).
    // =====================================================================================

    [Test]
    public void CancelledOnEntry_AbortsBeforePhaseOneAndWritesNothing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = Assert.Throws<OperationCanceledException>(
            () => { Emit(TwoModuleProject(), cancellationToken: cts.Token); },
            "A token already cancelled on entry did not abort EmitCore.");

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("phase 1"),
                "The abort did not happen at the first phase boundary. The checkpoint must be "
                + "the very first statement in EmitCore, before bin/<config> is created.");
            Assert.That(Directory.Exists(ObjGenDir), Is.False,
                "obj/gen was created despite the build being cancelled before it started.");
            Assert.That(Directory.Exists(Path.Combine(_dir, "bin")), Is.False,
                "bin/<config> was created despite the build being cancelled before it started.");
        });
    }

    /// <summary>
    /// Cancellation BETWEEN phases, not merely at entry — driven through the public
    /// <see cref="CppProjectBuilder.Build"/>, which is where phases 6 and 7 live. The toolchain
    /// resolver runs AFTER the obj/gen write (Task 8's pinned ordering), so cancelling from
    /// inside it proves the build ran all the way through phase 4 and then stopped at the next
    /// boundary instead of compiling. A single guard at the door would fail the second assertion.
    /// </summary>
    [Test]
    public void CancelledMidBuild_AbortsAfterTheEmitPhaseHasAlreadyRun()
    {
        using var cts = new CancellationTokenSource();
        var projectPath = WriteProject(TwoModuleProject());
        var probed = false;

        var ex = Assert.Throws<OperationCanceledException>(
            () =>
            {
                CppProjectBuilder.Build(ProjectFile.Load(projectPath), "Release",
                    resolveToolchain: () => { probed = true; cts.Cancel(); return FakeToolchain(); },
                    cancellationToken: cts.Token);
            },
            "Cancelling during the build did not abort it. Build must check the token at its "
            + "phase boundaries — at minimum before handing the request to the compiler.");

        Assert.Multiple(() =>
        {
            Assert.That(probed, Is.True,
                "The toolchain was never resolved, so the build was cancelled before it got "
                + "anywhere and this test is not exercising a MID-build cancellation.");
            Assert.That(ex!.Message, Does.Contain("phase 6"),
                "The abort did not happen at the compile boundary.");
            Assert.That(GeneratedFileNames(), Is.Not.Empty,
                "Nothing reached obj/gen, so this test would pass identically with a single guard "
                + "at EmitCore's entry.");
        });
    }

    /// <summary>
    /// The other half of the contract, and the one that protects every existing caller: an
    /// explicitly supplied, never-cancelled token must change nothing at all.
    /// </summary>
    [Test]
    public void UncancelledToken_ChangesNothing()
    {
        using var cts = new CancellationTokenSource();

        var (result, outcome) = Emit(TwoModuleProject(), cancellationToken: cts.Token);

        AssertNoErrors(result, "uncancelled token");
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Completed, Is.True);
            Assert.That(GeneratedFileNames(), Is.EqualTo(ExpectedGeneratedFileNames.OrderBy(n => n, StringComparer.Ordinal)),
                "Passing a live-but-uncancelled CancellationToken changed what the build wrote. "
                + "The checkpoints must be pure reads of IsCancellationRequested.");
        });
    }

    // =====================================================================================
    // 5. CleanGeneratedDir must cover every artifact NetProxyEmitter can write.
    // =====================================================================================

    /// <summary>
    /// <b>A drift invariant, not a restatement.</b> The oracle is
    /// <see cref="NetProxyEmitter.Emit"/>'s ACTUAL key set for a non-empty surface; the subject
    /// is <c>CppProjectBuilder.CleanGeneratedDir</c>'s filter. Four of the five artifacts escape
    /// the historical <c>.g.cpp</c>/<c>.g.h</c> suffix test (<c>blnet.h</c>,
    /// <c>blnet_runtime.hpp</c> and the two <c>.g.hpp</c> headers), so a project that STOPS using
    /// .NET would otherwise leave a removed member's proxy header on the include path where user
    /// C++ can still <c>#include</c> it. Goes red the moment a sixth artifact is added.
    /// </summary>
    [Test]
    public void CleanGeneratedDirRemovesEveryNetArtifact()
    {
        var objGen = Path.Combine(_dir, "obj", "gen");
        Directory.CreateDirectory(objGen);

        var artifacts = NetProxyEmitter.Emit(
            NetProxyEmitterTests.SixShapeSurface(), NetProxyEmitterTests.Module);
        Assert.That(artifacts, Is.Not.Empty, "The oracle produced no artifacts, so it proves nothing.");
        foreach (var kv in artifacts)
            File.WriteAllText(Path.Combine(objGen, kv.Key), kv.Value);

        var undeleted = CppProjectBuilder.CleanGeneratedDir(objGen);

        Assert.Multiple(() =>
        {
            Assert.That(undeleted, Is.Empty, "A generated file could not be deleted: "
                + string.Join(" | ", undeleted));
            Assert.That(Directory.EnumerateFiles(objGen).Select(Path.GetFileName), Is.Empty,
                "CleanGeneratedDir left a NetProxyEmitter artifact behind. Its filter and "
                + "NetProxyEmitter's file-name constants have drifted apart — add the missing name "
                + "to CppProjectBuilder.NetArtifactFileNames (read the constants off "
                + "NetProxyEmitter; do NOT widen the suffix test, which would start deleting user "
                + "headers).");
        });
    }

    /// <summary>
    /// The other side of the same filter: it must not eat a hand-written file that happens to
    /// share the directory. Pins that the widened filter added exact NAMES, never a new SUFFIX
    /// class — a <c>.h</c> or <c>.hpp</c> that is not one of the five survives.
    /// </summary>
    [Test]
    public void CleanGeneratedDirLeavesFilesItDoesNotOwn()
    {
        var objGen = Path.Combine(_dir, "obj", "gen");
        Directory.CreateDirectory(objGen);
        var survivors = new[] { "helper.h", "vendor.hpp", "notes.txt", "blnet_extra.hpp", "thing.cpp" };
        foreach (var name in survivors)
            File.WriteAllText(Path.Combine(objGen, name), "// user\n");
        File.WriteAllText(Path.Combine(objGen, "Stale.g.h"), "// generated\n");

        CppProjectBuilder.CleanGeneratedDir(objGen);

        Assert.That(Directory.EnumerateFiles(objGen).Select(Path.GetFileName)
                .OrderBy(n => n, StringComparer.Ordinal),
            Is.EqualTo(survivors.OrderBy(n => n, StringComparer.Ordinal)),
            "CleanGeneratedDir deleted a file it does not own. The .NET half of its filter must "
            + "match EXACT names from NetProxyEmitter's constants — never a suffix like \".hpp\", "
            + "which would take user headers with it.");
    }
}
