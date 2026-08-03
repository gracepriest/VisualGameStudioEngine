using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using BasicLang.Compiler;             // Lexer / Parser
using BasicLang.Compiler.CodeGen.Net;
using BasicLang.Compiler.ProjectSystem;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Net;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Shared scaffolding for P2a-2 Task 7b's two fixtures: a temp project directory, the
/// <c>.blproj</c> template, and a Roslyn-emitted probe assembly.
///
/// <para><b>Why a probe assembly rather than a framework type.</b> Two of the tests below need a
/// member with a property no framework type offers on demand — one deliberately marked
/// <c>[RequiresDynamicCode]</c> so ILC has something to complain about, and one with a surface
/// small enough that a <c>&lt;NetProxy&gt;</c> expansion is entirely predictable. Reaching for a
/// real BCL type instead would make the test hostage to that type's whole public surface: one
/// member the generator cannot yet spell turns a phase-5 test red for a reason phase 5 did not
/// cause.</para>
/// </summary>
internal static class NetShimPipelineFixture
{
    internal const string ProbeNamespace = "Aot.Probe";
    internal const string ProbeAssemblyName = "AotProbeLib";

    /// <summary>
    /// The probe assembly's source.
    ///
    /// <para><c>Risky</c> is what makes a BL6020 possible at all: <c>[RequiresDynamicCode]</c> is
    /// the annotation §15.10 pins to ERROR, and ILC reports it against the member that CALLS it —
    /// which, for a BasicLang call site, is the generated wrapper, i.e. §11.3's tier 1. Note it is
    /// reachable only through a BasicLang CALL: <c>NetSurfaceCollector</c>'s §7.2 filter
    /// BL6026-omits an AOT-hostile member from a DECLARED surface, so a <c>&lt;NetProxy&gt;</c>
    /// over this type would never carry it.</para>
    ///
    /// <para><c>Widget</c> is the opposite: three members with nothing hostile and nothing exotic,
    /// so its full §7.2 expansion is small and every member has a §8.3 wire form.</para>
    ///
    /// <para>The explicit <c>TargetFramework</c> attribute is not decoration — the shim csproj
    /// references this DLL by <c>&lt;HintPath&gt;</c>, and an assembly with no framework identity
    /// is the kind of input that produces an NU/CS warning nobody can act on.</para>
    /// </summary>
    internal const string ProbeSource = """
        using System.Diagnostics.CodeAnalysis;

        [assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v8.0")]

        namespace Aot.Probe
        {
            public static class AotProbe
            {
                [RequiresDynamicCode("Probe: pinned AOT-hostile member for the BL6020 test.")]
                public static int Risky(int n) => n + 1;
            }

            public class Widget
            {
                public Widget(int seed) { Value = seed; }
                public int Value { get; set; }
                public int Twice() => Value * 2;
            }
        }
        """;

    /// <summary>
    /// Compiles <see cref="ProbeSource"/> into <paramref name="directory"/>, <b>against the
    /// net8.0 REFERENCE assemblies</b>.
    ///
    /// <para>The reference pack is not a detail. Compiling against the shared framework's
    /// IMPLEMENTATION assemblies — which is what <c>NetTypeResolverTestRefs.FrameworkPaths</c> is,
    /// because the analyzer wants the set the runtime actually has — bakes a direct reference to
    /// <c>System.Private.CoreLib</c> into the probe's metadata. The generated shim then compiles
    /// against net8.0 ref assemblies, where that assembly does not exist, and every use of the
    /// probe's types fails with CS0012. No assembly a real user references is built that way, so
    /// the fixture must not build one either.</para>
    /// </summary>
    internal static string EmitProbeAssembly(string directory)
    {
        var references = ReferencePackAssemblies();
        if (references == null)
            Assert.Ignore("No net8.0 reference pack (Microsoft.NETCore.App.Ref) found beside the "
                          + "running runtime; the probe assembly cannot be built the way a real "
                          + "user reference is.");

        var path = Path.Combine(directory, ProbeAssemblyName + ".dll");
        var compilation = CSharpCompilation.Create(
            ProbeAssemblyName,
            new[] { CSharpSyntaxTree.ParseText(ProbeSource) },
            references!.Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Microsoft.CodeAnalysis.Emit.EmitResult emit;
        using (var stream = File.Create(path))
            emit = compilation.Emit(stream);

        Assert.That(emit.Success, Is.True,
            "the fixture's own probe assembly failed to compile, so nothing below means anything: "
            + string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    /// <summary>
    /// <c>&lt;dotnet root&gt;/packs/Microsoft.NETCore.App.Ref/&lt;8.x&gt;/ref/net8.0</c>, walked
    /// up from the running runtime directory, or null. The pack is necessarily installed on any
    /// machine that can publish the shim at all — the shim csproj targets
    /// <see cref="NetShimGenerator.TargetFramework"/> — so a null here means the machine cannot
    /// run these tests rather than that the product is wrong.
    /// </summary>
    private static IReadOnlyList<string>? ReferencePackAssemblies()
    {
        // .../dotnet/shared/Microsoft.NETCore.App/<version>  ->  .../dotnet
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (string.IsNullOrEmpty(runtimeDir)) return null;
        var dotnetRoot = Path.GetFullPath(Path.Combine(runtimeDir, "..", "..", ".."));

        var packRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        if (!Directory.Exists(packRoot)) return null;

        var tfm = NetShimGenerator.TargetFramework;              // "net8.0"
        var major = tfm.Substring("net".Length).Split('.')[0];   // "8"
        var pack = Directory.EnumerateDirectories(packRoot)
            .Where(d => Path.GetFileName(d).StartsWith(major + ".", StringComparison.Ordinal))
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .LastOrDefault();
        if (pack == null) return null;

        var refDir = Path.Combine(pack, "ref", tfm);
        return Directory.Exists(refDir)
            ? Directory.EnumerateFiles(refDir, "*.dll").ToList()
            : null;
    }

    /// <summary>
    /// The <c>.blproj</c>. Written with <c>File.WriteAllText</c>, never <c>ProjectFile.Save</c> —
    /// <c>XDocument.Save</c> injects a BOM.
    /// </summary>
    internal static string WriteProject(
        string directory, string projectName, string itemGroupXml = "", string outputType = "Exe")
    {
        var path = Path.Combine(directory, projectName + ".blproj");
        File.WriteAllText(path, $"""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>{projectName}</ProjectName>
                <OutputType>{outputType}</OutputType>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
              {itemGroupXml}
            </BasicLangProject>
            """);
        return path;
    }

    internal static string ReferenceItemGroup(string probeDllPath) => $"""
          <ItemGroup>
            <Reference Include="{ProbeAssemblyName}">
              <HintPath>{probeDllPath}</HintPath>
            </Reference>
          </ItemGroup>
        """;

    internal static string NewTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    internal static void TryDeleteDir(string dir)
    {
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(dir, recursive: true); return; }
            catch (IOException) { Thread.Sleep(300); }
            catch (UnauthorizedAccessException) { Thread.Sleep(300); }
        }
    }

    /// <summary>Runs a built executable and returns its stdout with line endings normalized.</summary>
    internal static string Run(string exePath)
    {
        var psi = new ProcessStartInfo(exePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
        };
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.That(process.WaitForExit(60_000), Is.True, "the built program did not exit.");
        Assert.That(process.ExitCode, Is.EqualTo(0),
            "the built program exited " + process.ExitCode
            + ". Exit code 3 is NetProxyEmitter.StartupFailureExitCode — the §9.3 handshake failed, "
            + "which usually means the shim DLL was not deployed next to the exe (phase 7) or its "
            + "exports do not match the proxy table.\nstdout:\n" + stdout + "\nstderr:\n" + stderr);
        return stdout.Replace("\r\n", "\n");
    }

    internal static string Diagnostics(CppProjectBuildResult result) =>
        string.Join("\n", result.Diagnostics.Select(
            d => (d.IsWarning ? "warning " : "error ") + d.Code + ": " + d.Message));
}

/// <summary>
/// P2a-2 Task 7b — the parts of spec §10.1's phase 5 that can be proven without spawning
/// <c>dotnet publish</c>: the two skip rules, §15.10's severity split through the phase-5 merge,
/// §11.3's provenance plumbing, and the three Task-7a handoff checks.
///
/// <para>The publish-backed proofs — including the milestone itself — live in
/// <see cref="NetShimPipelineTests"/>.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class NetShimPhaseTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp() => _dir = NetShimPipelineFixture.NewTempDir("blnet-phase5-");

    [TearDown]
    public void TearDown() => NetShimPipelineFixture.TryDeleteDir(_dir);

    private static readonly Lazy<NetTypeResolver> SharedResolver =
        new(() => NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths));

    /// <summary>Resolvable but never run — the NetBuildPipelineTests pattern.</summary>
    private static CppToolchain FakeToolchain() =>
        CppToolchain.FromExplicit("llvm", Path.Combine(Path.GetTempPath(), "no-such-clang++.exe"));

    private (CppProjectBuildResult Result, CppEmitOutcome Outcome) Emit(
        string itemGroupXml, IReadOnlyDictionary<string, string> files, bool forIntelliSense = false)
    {
        foreach (var file in files)
            File.WriteAllText(Path.Combine(_dir, file.Key), file.Value);

        var projectPath = NetShimPipelineFixture.WriteProject(_dir, "PhaseProbe", itemGroupXml);
        var result = new CppProjectBuildResult();
        // publishShim is left at its PRODUCTION default here on purpose: these tests are about
        // whether phase 5 runs at all, so switching it off would make every one of them vacuous.
        var outcome = CppProjectBuilder.EmitCore(
            ProjectFile.Load(projectPath), "Release", result,
            resolveToolchain: FakeToolchain, forIntelliSense: forIntelliSense);
        return (result, outcome);
    }

    private bool ShimArtifactsExist() =>
        Directory.Exists(Path.Combine(_dir, "obj", "gen", "shim"))
        || Directory.Exists(NetShimCache.CacheRoot(_dir));

    // =====================================================================================
    // 1. The two skip rules (spec §10.1).
    // =====================================================================================

    /// <summary>
    /// The inertness half, and the one that covers every project that existed before P2a-2: an
    /// empty surface means phase 5 does nothing whatsoever — no generated shim sources, no cache
    /// directory, no <see cref="CppEmitOutcome.ShimDllPath"/>, and above all no publish.
    /// </summary>
    [Test]
    public void EmptySurface_SkipsPhaseFiveEntirely()
    {
        var (result, outcome) = Emit("", new Dictionary<string, string>
        {
            ["Program.bas"] = """
                Module Program
                 Sub Main()
                  Console.WriteLine(21 + 21)
                 End Sub
                End Module
                """,
        });

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Completed, Is.True, NetShimPipelineFixture.Diagnostics(result));
            Assert.That(outcome.Surface!.IsNonEmpty, Is.False,
                "the probe program must not draw a .NET surface, or this test proves nothing.");
            // The POSITIVE signal, and the reason it exists: every phase-5 skip looks identical
            // from outside (no shim path, nothing on disk), so the two assertions below would stay
            // green if someone put `publishShim: false` on this fixture's Emit helper for speed.
            // Naming the REASON makes that impossible — the seam reports DisabledForTest.
            Assert.That(outcome.PhaseFive, Is.EqualTo(NetShimPhaseOutcome.SkippedEmptySurface),
                "phase 5 must skip because the SURFACE is empty. DisabledForTest here means this "
                + "fixture turned the seam off and the assertions below prove nothing.");
            Assert.That(outcome.ShimDllPath, Is.Null,
                "phase 5 produced a shim path for a project with no .NET at all.");
            Assert.That(ShimArtifactsExist(), Is.False,
                "phase 5 touched the file system for an EMPTY surface. The whole block must be a "
                + "no-op there — this is what keeps every pre-P2a-2 project's build byte-identical, "
                + "and a stray obj/gen/shim/*.csproj is something an IDE or a `dotnet build` sweep "
                + "will pick up.");
        });
    }

    /// <summary>
    /// §10.1's table makes "phases 1-4 give full C++ IntelliSense at zero publish cost" a promise:
    /// a project WITH a real .NET surface must still get its proxy headers on the IntelliSense
    /// path and must never wait on an AOT publish. A regression here is not subtle — every
    /// keystroke in the editor would spawn <c>dotnet publish</c>.
    /// </summary>
    [Test]
    public void ForIntelliSense_NeverPublishes_EvenWithARealSurface()
    {
        var probe = NetShimPipelineFixture.EmitProbeAssembly(_dir);
        var (result, outcome) = Emit(
            NetShimPipelineFixture.ReferenceItemGroup(probe)
            + "\n  <ItemGroup>\n    <NetProxy Include=\"Aot.Probe.Widget\" />\n  </ItemGroup>",
            new Dictionary<string, string> { ["main.cpp"] = "int main() { return 0; }\n" },
            forIntelliSense: true);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Completed, Is.True, NetShimPipelineFixture.Diagnostics(result));
            Assert.That(outcome.Surface!.IsNonEmpty, Is.True,
                "the <NetProxy> must have produced a real surface, or the skip below is vacuous.");
            Assert.That(
                File.Exists(Path.Combine(_dir, "obj", "gen", NetProxyEmitter.ProxiesFileName)),
                Is.True,
                "IntelliSense lost its proxy headers. Phases 1-4 run on BOTH paths — the proxy "
                + "header is pure C++ declarations and needs no shim to exist, which is exactly why "
                + "§10.1 can promise clangd completions at zero publish cost.");
            // Same seam-proofing as the empty-surface test above: this must skip because it is the
            // INTELLISENSE path, not because a fixture turned publishShim off.
            Assert.That(outcome.PhaseFive, Is.EqualTo(NetShimPhaseOutcome.SkippedForIntelliSense),
                "DisabledForTest here means the seam did the skipping and §10.1's promise is no "
                + "longer under test at all.");
            Assert.That(outcome.ShimDllPath, Is.Null);
            Assert.That(ShimArtifactsExist(), Is.False,
                "the IntelliSense path generated or published a shim. §10.1's phase table says "
                + "phase 5 is the FIRST phase IntelliSense does not run; F5 is the only thing that "
                + "ever waits.");
        });
    }

    // =====================================================================================
    // 2. §15.10's severity split, through the phase-5 merge, on REAL captured ILC text.
    // =====================================================================================

    // Copied verbatim from AotDiagnosticMapperTests, which is this repo's register of REAL
    // captured ILC output (see that fixture's remarks for how it was produced and why the only
    // edit is the shortened scratch path). What is under test here is not the parser — that
    // fixture owns it — but CppProjectBuilder's MERGE: the severity a BL6020 lands with, the
    // location it carries, and whether it fails the build.
    private const string Il3050AgainstWrapper =
        @"C:\p\Shim\Exports.g.cs(18): AOT analysis warning IL3050: BlnetTestShim.Exports.bl_net_System_Type_MakeGenericType_5c3f9a21(UInt64,UInt64,UInt64*): Using member 'System.Type.MakeGenericType(Type[])' which has 'RequiresDynamicCodeAttribute' can break functionality when AOT compiling. The native code for this instantiation might not be available at runtime. [C:\p\Shim\ShimSample.csproj]";

    private const string Il2026AgainstWrapper =
        @"C:\p\Shim\Exports.g.cs(31): Trim analysis warning IL2026: BlnetTestShim.Exports.bl_net_System_Type_MakeGenericType_5c3f9a21(UInt64,UInt64,UInt64*): Using member 'System.Text.Json.JsonSerializer.Serialize<Int32>(Int32,JsonSerializerOptions)' which has 'RequiresUnreferencedCodeAttribute' can break functionality when trimming application code. [C:\p\Shim\ShimSample.csproj]";

    private const string Il3053Aggregate =
        @"C:\p\libs\Contoso.Serialization.dll : warning IL3053: Assembly 'Contoso.Serialization' produced AOT analysis warnings. [C:\p\Shim\ShimSample.csproj]";

    /// <summary>The wrapper name the captured lines carry, as ILC printed it.</summary>
    private const string CapturedWrapperName = "bl_net_System_Type_MakeGenericType_5c3f9a21";

    [Test]
    public void PhaseFiveMerge_AppliesSection1510SeverityAndCarriesTheBasLocation()
    {
        // A mangled name carries a signature hash, so a surface whose export happens to BE
        // CapturedWrapperName cannot be hand-built. Rather than defeat the export filter in
        // NetShimGenerator.Provenance — the thing that stops a BL6020 naming a wrapper the shim
        // does not contain — the captured lines have their wrapper name substituted for one a real
        // surface really produces, through the same seam production reads. Only that token is
        // edited; every other character of the captured text stands.
        var surface = NetProxyEmitterTests.OneMemberSurface();
        var wrapper = NetShimGenerator.SurfaceDerivedExportNames(surface).Single();
        string Retarget(string line) => line.Replace(CapturedWrapperName, wrapper);

        var provenance = new List<KeyValuePair<string, NetWrapperOrigin>>
        {
            new(wrapper,
                NetWrapperOrigin.BasCallSite(@"C:\game\MyGame.bas", 42, 17, "Type.MakeGenericType")),
        };
        var result = new CppProjectBuildResult { Success = true };

        var errors = CppProjectBuilder.MergeAotDiagnostics(
            Retarget(Il3050AgainstWrapper) + "\n" + Retarget(Il2026AgainstWrapper)
                + "\n" + Il3053Aggregate,
            surface, compilation: null, declaredProvenance: provenance,
            referencePaths: new[] { @"C:\p\libs\Contoso.Serialization.dll" },
            projectFilePath: @"C:\game\MyGame.blproj", result: result);

        var byIl = result.Diagnostics.ToDictionary(
            d => d.Message.Contains("IL3050") ? "IL3050"
               : d.Message.Contains("IL2026") ? "IL2026" : "IL3053");

        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics.Select(d => d.Code), Is.All.EqualTo("BL6020"),
                "every mapped ILC diagnostic is BL6020 (§11.4's row for the AOT ceiling).");

            // §15.10, by DIAGNOSTIC CLASS — not by tier.
            Assert.That(byIl["IL3050"].IsWarning, Is.False,
                "IL3050 is RequiresDynamicCode: the call THROWS at runtime, so building would ship "
                + "a known crash. §15.10 pins it to error.");
            Assert.That(byIl["IL2026"].IsWarning, Is.True,
                "IL2026 is trim analysis: Microsoft documents many of these as conservative and "
                + "not end-developer actionable. §15.10 pins them to warning.");
            Assert.That(byIl["IL3053"].IsWarning, Is.True,
                "IL3053 is an assembly-level AGGREGATE. It is IL3xxx and would be caught by the "
                + "error rule, but §15.10 pins both aggregates to warning: an aggregate does not "
                + "prove the program's own paths break.");

            // §11.3 tier 1: the location is the .bas line, carried STRUCTURALLY so the IDE error
            // list can navigate to it, and NOT repeated inside the message text.
            Assert.That(byIl["IL3050"].FilePath, Is.EqualTo(@"C:\game\MyGame.bas"));
            Assert.That(byIl["IL3050"].Line, Is.EqualTo(42));
            Assert.That(byIl["IL3050"].Column, Is.EqualTo(17));
            Assert.That(byIl["IL3050"].Message, Does.Not.Contain("MyGame.bas(42,17)"),
                "the location is on FilePath/Line/Column, so Format(includeLocation: false) must "
                + "keep it out of the message — otherwise every renderer prints it twice.");
            Assert.That(byIl["IL3050"].Message, Does.Contain("MakeGenericType"));
            Assert.That(byIl["IL3050"].Message, Does.Contain(AotDiagnosticMapper.Remedy));

            // Tier 3 has no location at all and is attributed to the project, like every other
            // project-level finding this builder produces.
            Assert.That(byIl["IL3053"].FilePath, Is.EqualTo(@"C:\game\MyGame.blproj"));
            Assert.That(byIl["IL3053"].Line, Is.EqualTo(0));

            Assert.That(errors, Is.EqualTo(1), "exactly one of the three is an error.");
            Assert.That(result.Success, Is.False,
                "a BL6020 ERROR must fail the build — otherwise the user ships a binary whose .NET "
                + "call is known to throw.");
        });
    }

    /// <summary>
    /// <b>The merge ORDER, which reads backwards and was wrong on first write.</b>
    /// <c>NetProvenanceMap</c> is last-write-wins, so the source whose location should be shown
    /// must be appended LAST. A member reachable BOTH ways — a <c>&lt;NetProxy Include="X"/&gt;</c>
    /// plus a <c>.bas</c> call into <c>X</c>, which mangle identically because both descriptors
    /// come from <c>NetTypeResolver.Describe</c> — must report at the <c>.bas</c> line the user
    /// wrote, not at the <c>.blproj</c> item, whose origin carries no line at all.
    ///
    /// <para>Every other provenance test here exercises ONE source, so none of them can see this:
    /// reversing the two <c>AddRange</c> calls in <c>MergeAotDiagnostics</c> leaves them all green
    /// and turns §11.3's tier 1 into a file-only attribution. Mutation-verified.</para>
    /// </summary>
    [Test]
    public void AMemberReachedBothWays_PrefersTheBasCallSiteOverTheNetProxyItem()
    {
        var surface = NetProxyEmitterTests.OneMemberSurface();
        var wrapper = NetShimGenerator.SurfaceDerivedExportNames(surface).Single();

        // The SAME mangled name from both sources, which is exactly what a project declaring a
        // type and also calling into it produces.
        var declared = new List<KeyValuePair<string, NetWrapperOrigin>>
        {
            new(wrapper, NetWrapperOrigin.NetProxyDeclaration(@"C:\game\MyGame.blproj", "MyLib.Widget")),
        };
        var compilation = new CompilationResult();
        compilation.NetCallSiteOriginsWritable.Add(new KeyValuePair<string, NetWrapperOrigin>(
            wrapper, NetWrapperOrigin.BasCallSite(@"C:\game\MyGame.bas", 42, 17, "Widget.Twice")));

        var result = new CppProjectBuildResult { Success = true };
        CppProjectBuilder.MergeAotDiagnostics(
            Il3050AgainstWrapper.Replace(CapturedWrapperName, wrapper),
            surface, compilation, declared,
            referencePaths: Array.Empty<string>(),
            projectFilePath: @"C:\game\MyGame.blproj", result: result);

        var diagnostic = result.Diagnostics.Single();
        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.FilePath, Is.EqualTo(@"C:\game\MyGame.bas"),
                "the .blproj declaration overwrote the .bas call site. NetProvenanceMap is "
                + "LAST-write-wins, so MergeAotDiagnostics must append declaredProvenance FIRST "
                + "and the call sites SECOND — the order reads backwards, which is how it came to "
                + "be inverted.");
            Assert.That(diagnostic.Line, Is.EqualTo(42),
                "a <NetProxy> origin carries no line, so losing the call site also loses the "
                + "position — the entire value of §11.3's tier 1.");
        });
    }

    /// <summary>
    /// <b>A benign line must never fail a green build.</b> <c>Map</c> scans every line of the
    /// publish output for a bare <c>ILxxxx</c> token, so a project under <c>C:\builds\IL3050\</c>
    /// — or any path, package or identifier containing one — produces an
    /// <see cref="AotAttribution.Unattributed"/> diagnostic. §11.3 says keep it; §15.10's severity
    /// rule would have made it an ERROR and failed a publish that succeeded, over a directory name.
    ///
    /// <para>The forced warning applies ONLY to lines the severity/code grammar could not parse.
    /// A parsed IL3xxx keeps its error severity — pinned by
    /// <see cref="PhaseFiveMerge_AppliesSection1510SeverityAndCarriesTheBasLocation"/>, which
    /// would go red if this were implemented as a blanket downgrade.</para>
    /// </summary>
    [Test]
    public void AnIlTokenInOrdinaryPublishChatter_IsAWarningAndDoesNotFailTheBuild()
    {
        var result = new CppProjectBuildResult { Success = true };

        var errors = CppProjectBuilder.MergeAotDiagnostics(
            @"  ProbeApp -> C:\builds\IL3050\bin\Release\net8.0\win-x64\ProbeApp.dll",
            NetProxyEmitterTests.OneMemberSurface(), compilation: null,
            declaredProvenance: null, referencePaths: Array.Empty<string>(),
            projectFilePath: @"C:\game\MyGame.blproj", result: result);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.EqualTo(0),
                "an unparsed line became a BL6020 ERROR. AotDiagnosticMapper.Unattributed must "
                + "force warning severity — a line the parser cannot vouch for as a diagnostic is "
                + "the last thing that should be trusted to prove the program is broken.");
            Assert.That(result.Success, Is.True,
                "a SUCCESSFUL publish was turned into a failed build by a directory name.");
            Assert.That(result.Diagnostics.Single().IsWarning, Is.True,
                "…and it is still REPORTED (§11.3 never drops an IL-coded line), just as a warning.");
        });
    }

    // =====================================================================================
    // 3. §11.3's provenance plumbing.
    // =====================================================================================

    /// <summary>
    /// The §7.2 half: a <c>&lt;NetProxy&gt;</c> expansion records a
    /// <see cref="NetWrapperOriginKind.NetProxyDeclaration"/> origin per member it contributes.
    /// Recorded in the collector because it is the last place that knows WHICH declaration a
    /// member came from — an inherited member's <c>DeclaringTypeFullName</c> is a BASE type, so
    /// the mapping cannot be recovered from <see cref="NetSurface"/> afterwards.
    /// </summary>
    [Test]
    public void DeclaredSurface_RecordsNetProxyProvenancePerMember()
    {
        var probe = NetShimPipelineFixture.EmitProbeAssembly(_dir);
        var project = new ProjectFile { Backend = "cpp" };
        project.AssemblyReferences.Add(new AssemblyReference
        { Name = NetShimPipelineFixture.ProbeAssemblyName, HintPath = probe });
        project.NetProxyTypes.Add("Aot.Probe.Widget");
        var projectPath = Path.Combine(_dir, "ProvenanceProbe.blproj");

        var closure = NetReferenceResolver.Resolve(project, projectPath);
        var provenance = new List<KeyValuePair<string, NetWrapperOrigin>>();
        var surface = NetSurfaceCollector.Collect(
            Array.Empty<BasicLang.Compiler.IR.IRModule>(), project,
            () => NetTypeResolver.Create(closure.All),
            new List<NetReferenceDiagnostic>(), provenance);

        Assert.Multiple(() =>
        {
            Assert.That(surface.Members, Is.Not.Empty, "the declared type expanded to nothing.");
            Assert.That(provenance.Select(p => p.Key),
                Is.EquivalentTo(surface.Members.Select(NetNameMangler.Mangle)),
                "one provenance entry per collected member, keyed by the SAME mangle the shim "
                + "generator exports under — a key set that drifts from the export set is a "
                + "BL6020 that names a wrapper the shim does not contain.");
            Assert.That(provenance.Select(p => p.Value.Kind), Is.All.EqualTo(
                NetWrapperOriginKind.NetProxyDeclaration),
                "a <NetProxy> member has no BasicLang call site; reporting one would send the user "
                + "hunting for something they never wrote.");
            Assert.That(provenance.Select(p => p.Value.Description), Is.All.EqualTo("Aot.Probe.Widget"),
                "the origin must name the DECLARATION the user wrote, not the member's declaring "
                + "type — those differ for every inherited member.");
        });
    }

    /// <summary>
    /// <c>NetShimGenerator.Provenance</c> is where the map is narrowed to the wrappers that are
    /// actually emitted. Both producers can legitimately offer an entry for a member that never
    /// becomes an export (a non-exact annotation, a BL6026-omitted member, a call the optimizer
    /// deleted), and an entry that survived would let a BL6020 point at a <c>.bas</c> line for a
    /// wrapper the shim does not contain.
    /// </summary>
    [Test]
    public void ProvenanceIsIntersectedWithTheEmittedExports()
    {
        var surface = NetProxyEmitterTests.OneMemberSurface();
        var exported = NetShimGenerator.SurfaceDerivedExportNames(surface).Single();

        var map = NetShimGenerator.Provenance(surface, new[]
        {
            new KeyValuePair<string, NetWrapperOrigin>(
                exported, NetWrapperOrigin.BasCallSite("A.bas", 1, 1, "kept")),
            new KeyValuePair<string, NetWrapperOrigin>(
                "bl_net_Not_An_Export_00000000", NetWrapperOrigin.BasCallSite("B.bas", 2, 2, "dropped")),
        });

        Assert.Multiple(() =>
        {
            Assert.That(map.Count, Is.EqualTo(1),
                "an origin for a name the shim does not export must not reach the map.");
            Assert.That(map.TryGet(exported, out var origin), Is.True);
            Assert.That(origin.Description, Is.EqualTo("kept"));
        });
    }

    // =====================================================================================
    // 4. Task-7a handoff check 1 — the last-error TYPE field carries the FULL chain.
    // =====================================================================================

    /// <summary>
    /// Spec §11.1's wire format, at the source level. The behavioural proof — a real shim, a real
    /// managed exception, a typed <c>Catch</c> of a BASE type — is
    /// <see cref="NetShimPipelineTests.TypedCatchOfABaseType_MatchesTheGeneratedShimsChain"/>;
    /// this test exists so the failure is legible when that one goes red.
    ///
    /// <para>Before Task 7b the generated <c>Fail(ex)</c> recorded
    /// <c>ex.GetType().FullName</c> — ONE name. <c>NetCheckTyped</c> matches by ';'-delimited
    /// element equality, so <c>Catch ex As Exception</c> around a call throwing
    /// <c>ArgumentNullException</c> silently stopped matching and the exception escaped the
    /// <c>Try</c> entirely. The 7a stub planted the chain by hand, which is exactly why nothing
    /// caught this.</para>
    /// </summary>
    [Test]
    public void GeneratedFail_RecordsTheFullInheritanceChain_NotJustTheTypeName()
    {
        var cs = NetShimGenerator.EmitExports(NetProxyEmitterTests.OneMemberSurface());

        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Contain("_lastErrorType = InheritanceChain(ex.GetType())"),
                "Fail(ex) must record the §11.1 CHAIN. `ex.GetType().FullName` is one name, and "
                + "NetCheckTyped matches ';'-delimited elements — with a single name every typed "
                + "Catch of a BASE type stops matching, silently.");
            Assert.That(cs, Does.Contain("t != typeof(object)"),
                "the walk must terminate at System.Exception, which is §11.1's worked example and "
                + "the last type a BasicLang Catch can name; System.Object in the chain is noise.");
            Assert.That(cs, Does.Contain("chain.Append(';')"),
                "§11.1 pins the separator to ';' — the native NetException.Matches splits on it.");
            Assert.That(cs, Does.Not.Contain("_lastErrorType = ex.GetType().FullName"),
                "the single-name form must be gone, not merely supplemented.");
        });
    }

    // =====================================================================================
    // 5. Task-7a handoff check 2 — a write to a read-only .NET property is refused, positioned.
    // =====================================================================================

    private static SemanticAnalyzer Analyze(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => SharedResolver.Value, nativeBackend: true);
        analyzer.Analyze(ast);
        return analyzer;
    }

    /// <summary>
    /// <c>Stream.CanRead</c> is get-only. Without this refusal the write synthesizes a
    /// <c>set_CanRead</c> accessor that names a member which does not exist, the collector mints a
    /// proxy slot and a shim export for it, and the generated <c>Exports.g.cs</c> spells
    /// <c>target.CanRead = value</c> — which fails in <c>csc</c> AFTER the ~27 s AOT publish, in a
    /// file under <c>obj/gen/shim</c>, with no BasicLang line anywhere in the message.
    /// </summary>
    [Test]
    public void WriteToAReadOnlyNetProperty_IsRefusedWithAPositionedBl6017()
    {
        var analyzer = Analyze("""
            Module M
             Sub Main()
              Dim st As Stream
              st.CanRead = True
             End Sub
            End Module
            """);

        var finding = analyzer.NetDiagnostics.FirstOrDefault(d => d.Code == "BL6017");

        Assert.Multiple(() =>
        {
            Assert.That(finding, Is.Not.Null,
                "a write to a get-only .NET property must be refused at the ASSIGNMENT, before "
                + "anything is emitted. Got: " + string.Join(" | ",
                    analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
            Assert.That(finding!.IsWarning, Is.False,
                "the native backend cannot lower this at all, so §6.3's flip makes it an error.");
            Assert.That(finding.Message, Does.Contain("CanRead"), "…and must name the member.");
            Assert.That(finding.Message, Does.Contain("no public, non-init setter"),
                "…and say what is actually wrong, so the fix is obvious. 'non-init' is load-bearing: "
                + "IsSettable is false for THREE shapes — no setter, a non-public setter, and an "
                + "init-only one — and an init property HAS a visible setter, so 'no public setter' "
                + "would send that user hunting for something the metadata plainly shows.");
            Assert.That(finding.Message, Does.Contain("(line 4)"),
                "POSITIONED. An unpositioned diagnostic about a one-line mistake in a 400-line "
                + "program is barely better than the csc failure it replaces.");
        });
    }

    /// <summary>
    /// The other half, and the one that stops the refusal from being a blanket ban:
    /// <c>Stream.Position</c> HAS a public setter, and writing it must still work — it is the
    /// shape <c>NetProxyStubRunTests.PropertyGetAndSet_RouteThroughTheAccessorSlots</c> runs
    /// end-to-end.
    /// </summary>
    [Test]
    public void WriteToAWritableNetProperty_IsStillAccepted()
    {
        var analyzer = Analyze("""
            Module M
             Sub Main()
              Dim st As Stream
              st.Position = 5
             End Sub
            End Module
            """);

        Assert.That(analyzer.NetDiagnostics, Is.Empty,
            "a write to a settable .NET property must produce no finding at all. If this went red "
            + "with BL6017, the settability probe is answering false for a property that has a "
            + "perfectly ordinary public setter — check NetTypeResolver.IsSettable, not the "
            + "analyzer: " + string.Join(" | ",
                analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));
    }

    /// <summary>
    /// <b>Which call site a BL6020 blames must be a rule, not an allocation accident.</b>
    /// <c>NetAstAnnotations</c>' backing dictionary is keyed by AST node REFERENCE, so its
    /// enumeration order follows address-derived hash codes and differs run to run; combined with
    /// <see cref="NetProvenanceMap"/>'s last-write-wins rule, a member called from several sites in
    /// one file would get a different <c>.bas</c> line in each build — a diagnostic that moves on
    /// its own between two identical builds.
    ///
    /// <para>Yielding in source order fixes the rule at "the LAST call site wins". This asserts the
    /// ORDER rather than trying to observe cross-run instability, which is not reproducible inside
    /// one process (hash codes are stable within it) — the order is the property that makes the
    /// rule true.</para>
    /// </summary>
    [Test]
    public void CallSiteOrigins_AreYieldedInSourceOrder_SoAttributionIsDeterministic()
    {
        var analyzer = Analyze("""
            Module M
             Sub Main()
              Dim st As Stream
              Dim p1 = st.Position
              Dim p2 = st.Position
              Dim p3 = st.Position
              Dim p4 = st.Position + st.Position
             End Sub
            End Module
            """);

        // (Line, Column), not just Line: line 7 carries TWO reads, so the secondary key is the
        // only thing ordering that pair. With three reads on three distinct lines the
        // comparator's Column clause never executes and deleting it leaves the test green.
        var positions = analyzer.NetCallSiteOrigins("Probe.bas", lineOffset: 0)
            .Select(o => (o.Value.Line, o.Value.Column)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(positions, Is.Not.Empty,
                "the four property-read statements recorded no annotations, so this proves nothing.");
            Assert.That(positions.Count(p => p.Line == 7), Is.GreaterThanOrEqualTo(2),
                "the two-reads-on-one-line pair is what exercises the Column tiebreak; if line 7 "
                + "contributed fewer than two entries the secondary key is still unproven.");
            Assert.That(positions, Is.Ordered,
                "CallSiteOrigins must yield in (Line, Column) order. Straight off the "
                + "reference-keyed dictionary the order is address-derived, and the last writer "
                + "into a last-write-wins map then changes between identical builds. Got: "
                + string.Join(", ", positions));
            Assert.That(positions.Last().Line, Is.EqualTo(7),
                "the LAST entry must be the last call site in the file — that is the stated rule "
                + "the provenance map's last-write-wins semantics turn into the blamed line.");
        });
    }

    // =====================================================================================
    // 6. Task-7a handoff check 3 — duplicate <NetProxy> declarations must not churn the key.
    // =====================================================================================

    private static NetShimCacheEnvironment Env() => new(
        "Release", NetShimGenerator.TargetFramework, NetShimPublisher.DefaultRuntimeIdentifier,
        "probe-toolchain", "9.9.999");

    [Test]
    public void DuplicateNetProxyDeclarations_ProduceTheSameCacheKey()
    {
        var member = NetProxyEmitterTests.OneMemberSurface().Members;
        var once = new NetSurface(member, new[] { "MyLib.Widget" });
        var twice = new NetSurface(member, new[] { "MyLib.Widget", "MyLib.Widget" });

        var references = Array.Empty<string>();
        var keyOnce = NetShimCache.KeyFor(once, references, Env(), "template");
        var keyTwice = NetShimCache.KeyFor(twice, references, Env(), "template");

        Assert.That(keyTwice, Is.EqualTo(keyOnce),
            "a .blproj may carry the same <NetProxy Include> twice (two ItemGroups, a copy-paste, "
            + "an appending targets file), and NetSurfaceCollector keeps DeclaredTypeNames VERBATIM "
            + "so a Library cannot skip BL6025 through a typo. But the collector expands each "
            + "DISTINCT Include exactly once, so the two surfaces produce the identical shim — "
            + "keying them apart costs a needless ~27 s cold publish. De-duplicate on the KEY side.");
    }

    // =====================================================================================
    // 7. §8.5's value-type receivers: the derivation itself (P2a-2 Task-8 Step 0, 7b-I8).
    // =====================================================================================

    private static NetMemberDescriptor Instance(string declaringType, string name) =>
        new(name, declaringType, NetMemberCategory.Method, isStatic: false, arity: 0,
            "System.Void", Array.Empty<NetParameterDescriptor>());

    /// <summary>
    /// <b>The path with the most expensive failure modes in this pipeline had no test at all.</b>
    /// <c>ValueTypeReceiverNames</c> decides which receivers the generated shim reaches through
    /// <c>Unsafe.Unbox&lt;T&gt;</c>; get it wrong on a struct and the shim either fails to
    /// compile (CS0445, if the member is a property SET) or compiles and mutates the temporary
    /// the unboxing produced — §8.5's <c>MoveNext</c>-forever loop, which is not a diagnostic.
    ///
    /// <para>It is a BY-NAME lookup, which is what makes it fragile: the spelling comes from
    /// <c>NetTypeResolver.TypeName</c> and has to round-trip back through
    /// <c>TypeSymbol</c>. Nested generic enumerators (<c>List&lt;T&gt;.Enumerator</c>,
    /// <c>Dictionary&lt;K,V&gt;.Enumerator</c>) are the highest-risk spellings on that path AND
    /// the ones Tasks 9/10 bring in, so they are asserted here rather than discovered there.</para>
    ///
    /// <para>Static members and constructors contribute NO receiver — a static call has none and
    /// a constructor's export returns the object rather than receiving one — so a value type
    /// reached only that way must not appear.</para>
    /// </summary>
    [Test]
    public void ValueTypeReceiverNames_IdentifiesFrameworkStructsAndNotClasses()
    {
        var surface = new NetSurface(
            new[]
            {
                Instance("System.DateTime", "ToLocalTime"),                 // struct
                Instance("System.Text.RegularExpressions.Regex", "IsMatch"),// class
                new NetMemberDescriptor(
                    "Now", "System.DateTime", NetMemberCategory.Property, isStatic: true,
                    arity: 0, "System.DateTime", Array.Empty<NetParameterDescriptor>()),
                new NetMemberDescriptor(
                    ".ctor", "System.Guid", NetMemberCategory.Constructor, isStatic: false,
                    arity: 0, "System.Void", Array.Empty<NetParameterDescriptor>()),
            },
            Array.Empty<string>());

        var (names, complete) = CppProjectBuilder.ValueTypeReceiverNames(
            surface, () => NetStubHarness.SharedResolver.Value);

        Assert.Multiple(() =>
        {
            Assert.That(complete, Is.True,
                "every receiver in this surface is a real framework type, so the resolver must "
                + "have answered for all of them. An incomplete answer poisons the shim cache "
                + "key (7b-I7), so a false 'incomplete' costs a ~25 s publish on every build.");
            Assert.That(names, Is.EquivalentTo(new[] { "System.DateTime" }),
                "the value-type receiver set is wrong. A STRUCT missing from it means the shim "
                + "reaches it through an unboxing cast: a property SET is then CS0445 (the whole "
                + "shim fails to compile) and a mutating method silently mutates the temporary — "
                + "§8.5's infinite MoveNext. A CLASS present in it is the opposite bug: "
                + "Unsafe.Unbox on a reference type. Static members and constructors contribute "
                + "no receiver at all — a static call has none, and a constructor's export "
                + "RETURNS the object rather than receiving one.");
        });
    }

    /// <summary>
    /// ⛔ <b>A HARD Task-9 PREREQUISITE, found by adding this fixture (P2a-2 Task-8 Step 0,
    /// 7b-I8) and pinned here rather than left to be discovered as an infinite loop.</b>
    ///
    /// <para><c>NetTypeResolver.TypeName</c> builds a nested type's spelling from
    /// <c>QualifiedName</c>, which walks containing types by NAME and drops their generic
    /// arity — so <c>List&lt;T&gt;.Enumerator</c> spells
    /// <c>System.Collections.Generic.List.Enumerator</c>, while the metadata name its own
    /// <c>Lookup</c> needs is <c>System.Collections.Generic.List`1+Enumerator</c>. Verified
    /// both ways in this test: the produced spelling does NOT resolve, the metadata form does.
    /// The surface collector admits <c>GetEnumerator()</c> quite happily (the nested type has
    /// arity 0 of its own, so the open-type-parameter check never fires), so the descriptor is
    /// real and reaches the generator.</para>
    ///
    /// <para><b>Two consequences, and Task 9 hits both.</b> (1) The receiver lookup answers
    /// "not a value type" for every enumerator struct, so the shim reaches it through
    /// <c>((List&lt;int&gt;.Enumerator)o!)</c> — §8.5's mutate-the-temporary
    /// <c>MoveNext</c>-forever loop, which is not a diagnostic. (2)
    /// <c>NetShimGenerator.Qualified</c> would emit
    /// <c>global::System.Collections.Generic.List.Enumerator</c>, which is not valid C# either.
    /// The fix belongs with §8.5: either teach <c>TypeName</c>/<c>CandidateMetadataNames</c> the
    /// arity form, or derive the value-type set from the <c>ITypeSymbol</c> the COLLECTOR
    /// already holds (which <c>NetShimGenerator.Emit</c>'s own parameter docs call the only
    /// thing that can really know). Flip this test when it lands.</para>
    ///
    /// <para>Until then the 7b-I7 poisoning is what keeps the damage bounded: the answer is
    /// INCOMPLETE, so a wrong shim can never be committed to the cache and hit forever.</para>
    /// </summary>
    [Test]
    public void ValueTypeReceiverNames_CannotSeeANestedGenericStruct_Task9Prerequisite()
    {
        var resolver = NetStubHarness.SharedResolver.Value;

        // Derived, not hard-coded: this test is about a spelling round-tripping through the
        // resolver, so both halves must come from the resolver itself.
        var getEnumerator = resolver.GetMembers("System.Collections.Generic.List`1")
            .FirstOrDefault(m => m.Name == "GetEnumerator" && m.Parameters.Count == 0);
        Assert.That(getEnumerator, Is.Not.Null,
            "fixture provenance: List<T>.GetEnumerator() must be in the collected surface, or "
            + "this test is no longer describing the shape Task 9 will hit.");
        var enumeratorSpelling = getEnumerator!.TypeFullName;

        var surface = new NetSurface(
            new[] { Instance(enumeratorSpelling, "MoveNext") }, Array.Empty<string>());
        var (names, complete) = CppProjectBuilder.ValueTypeReceiverNames(
            surface, () => resolver);

        Assert.Multiple(() =>
        {
            Assert.That(
                resolver.ResolveTypeDetailed(enumeratorSpelling).Outcome,
                Is.EqualTo(NetTypeLookupOutcome.NotFound),
                $"'{enumeratorSpelling}' is what NetTypeResolver.TypeName produced for "
                + "List<T>.Enumerator, and its own Lookup cannot resolve it. If this now "
                + "RESOLVES, the arity round-trip was fixed — flip this whole test to assert "
                + "that the enumerator IS reported as a value type.");
            Assert.That(
                resolver.ResolveTypeDetailed("System.Collections.Generic.List`1+Enumerator").Outcome,
                Is.EqualTo(NetTypeLookupOutcome.Resolved),
                "the METADATA spelling resolves, which is what makes this a naming defect rather "
                + "than a missing framework reference.");
            Assert.That(names, Is.Empty,
                "today the enumerator struct is invisible to the receiver set — §8.5's infinite "
                + "MoveNext, waiting for Task 9.");
            Assert.That(complete, Is.False,
                "and it MUST at least report incomplete, so the wrong shim is never cached "
                + "(7b-I7). If this goes true while the lookup still fails, the poisoning was "
                + "removed and the bug became permanent.");
        });
    }

    /// <summary>
    /// The poisoning half (7b-I7). A receiver the resolver cannot answer for must report
    /// INCOMPLETE rather than silently reading as "reference type" — that answer decides shim
    /// CONTENT, is outside §10.2's cache key, and framework paths are deliberately excluded from
    /// the key, so a wrong-but-compiling shim would be committed and hit forever. The cache is
    /// allowed to be absent, never allowed to be wrong.
    /// </summary>
    [Test]
    public void ValueTypeReceiverNames_ReportsIncomplete_WhenAReceiverDoesNotResolve()
    {
        var surface = new NetSurface(
            new[] { Instance("No.Such.Namespace.MissingReceiver", "Frob") },
            Array.Empty<string>());

        var (names, complete) = CppProjectBuilder.ValueTypeReceiverNames(
            surface, () => NetStubHarness.SharedResolver.Value);

        Assert.Multiple(() =>
        {
            Assert.That(complete, Is.False,
                "an unresolvable receiver must POISON the answer. Treating it as a reference "
                + "type is the silent degradation: if it was really a struct, the committed shim "
                + "is wrong and every later build hits it.");
            Assert.That(names, Is.Empty,
                "an unresolvable receiver must not be guessed INTO the value-type set either.");
        });
    }

    /// <summary>
    /// A surface with no instance receivers at all is COMPLETE, not incomplete: there is nothing
    /// to be wrong about, and answering "incomplete" would poison the cache key for every
    /// static-only surface — a permanent ~25 s tax on a program that never boxes a receiver.
    /// </summary>
    [Test]
    public void ValueTypeReceiverNames_WithNoReceivers_IsCompleteAndEmpty()
    {
        var surface = new NetSurface(
            new[]
            {
                new NetMemberDescriptor(
                    "Escape", "System.Text.RegularExpressions.Regex", NetMemberCategory.Method,
                    isStatic: true, arity: 0, "System.String",
                    new[] { new NetParameterDescriptor(NetRefKind.None, "System.String") }),
            },
            Array.Empty<string>());

        var (names, complete) = CppProjectBuilder.ValueTypeReceiverNames(surface, () => null);

        Assert.Multiple(() =>
        {
            Assert.That(complete, Is.True,
                "no receivers means nothing to resolve, so the answer is complete even with no "
                + "resolver at all — otherwise every static-only surface pays a needless publish.");
            Assert.That(names, Is.Empty);
        });
    }

    /// <summary>
    /// The other half: de-duplicating must not erase the component. Two surfaces with genuinely
    /// different declarations still key apart — otherwise the "fix" above would be a false-hit
    /// machine, which is the one failure mode <see cref="NetShimCache"/> resolves every ambiguity
    /// against.
    /// </summary>
    [Test]
    public void DifferentNetProxyDeclarations_StillProduceDifferentCacheKeys()
    {
        var member = NetProxyEmitterTests.OneMemberSurface().Members;
        var a = new NetSurface(member, new[] { "MyLib.Widget" });
        var b = new NetSurface(member, new[] { "MyLib.Widget", "MyLib.Gadget" });

        Assert.That(
            NetShimCache.KeyFor(b, Array.Empty<string>(), Env(), "template"),
            Is.Not.EqualTo(NetShimCache.KeyFor(a, Array.Empty<string>(), Env(), "template")),
            "the declared-type set must still be IN the key. A dedup that collapsed it would make "
            + "adding a <NetProxy> a silent cache hit on the old shim.");
    }
}

/// <summary>
/// <b>P2a-2 Task 7b — THE MILESTONE.</b> These are the first tests in this repo in which a .NET
/// call executes from a natively-compiled BasicLang program: BasicLang → C++ → native exe →
/// AOT-published managed shim → the real BCL, with no .NET runtime installed on the target.
///
/// <para>Every test here drives <see cref="CppProjectBuilder.Build"/> — the ONE native build path,
/// shared verbatim by the CLI (<c>Program.cs</c>) and the IDE (<c>BuildService</c>), so both entry
/// points are covered by construction. Each spawns a real <c>dotnet publish</c> (~11-27 s
/// measured), hence <c>[Category("Integration")]</c> and <c>[NonParallelizable]</c>.</para>
///
/// <para><b>Known pre-existing divergence these tests deliberately pin rather than work around:</b>
/// <c>Console.WriteLine(someBoolean)</c> prints <c>1</c>/<c>0</c> on the C++ backend instead of
/// .NET's <c>True</c>/<c>False</c> (raw <c>&lt;&lt;</c> of a bool) — item 8 of
/// <c>docs/superpowers/specs/2026-07-07-cpp-backend-preexisting-gaps.md</c>, reproduced here on a
/// program with no .NET in it at all. It is a Boolean FORMATTING gap in the C++ BCL layer and has
/// nothing to do with the boundary, so the milestone program takes its <c>True</c> through an
/// <c>If</c> — which is also the stronger oracle, since a wrong answer changes the BRANCH rather
/// than the spelling — and pins the raw form on the negative case.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class NetShimPipelineTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("The shim publish recipe is pinned to win-x64 (spec §8.1).");
        if (CppToolchain.Find() == null)
            Assert.Ignore("No C++ toolchain available (clang++/g++/MSVC).");
        _dir = NetShimPipelineFixture.NewTempDir("blnet-pipeline5-");
    }

    [TearDown]
    public void TearDown()
    {
        if (_dir != null) NetShimPipelineFixture.TryDeleteDir(_dir);
    }

    private (CppProjectBuildResult Result, TimeSpan Elapsed) Build(
        string projectName, string itemGroupXml = "")
    {
        var projectPath = NetShimPipelineFixture.WriteProject(_dir, projectName, itemGroupXml);
        var stopwatch = Stopwatch.StartNew();
        var result = CppProjectBuilder.Build(ProjectFile.Load(projectPath), "Release");
        stopwatch.Stop();
        TestContext.Out.WriteLine(
            $"{projectName}: build took {stopwatch.Elapsed.TotalSeconds:F1}s");
        foreach (var message in result.Messages)
            TestContext.Out.WriteLine("  " + message);
        return (result, stopwatch.Elapsed);
    }

    private void Write(string name, string content) =>
        File.WriteAllText(Path.Combine(_dir, name), content);

    // -------------------------------------------------------------------------------------
    // Shared builds (P2a-2 Task-8 Step 0, 7b-I9).
    // -------------------------------------------------------------------------------------

    /// <summary>A program built once and reusable by any test in this fixture.</summary>
    internal sealed record SharedBuild(
        string Directory, CppProjectBuildResult Result, TimeSpan Elapsed);

    private static readonly ConcurrentDictionary<string, Lazy<SharedBuild>> SharedBuilds =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentBag<string> SharedDirs = new();

    /// <summary>
    /// Builds <paramref name="files"/> as <paramref name="projectName"/> ONCE per distinct
    /// (name, item group, sources) tuple and hands every later caller the same result.
    ///
    /// <para><b>Why this affordance exists, and why the obvious alternative does not work.</b>
    /// This fixture's cost is strictly linear: a fresh directory and a real
    /// <c>dotnet publish</c> per test (~11-27 s measured), five today and one more per scenario
    /// as Tasks 9-12 land. Sharing a DIRECTORY would not help — §10.2's cache key hashes the
    /// mangled member set, so a different program is a guaranteed miss — which leaves exactly
    /// one lever: FEWER, RICHER programs, several assertions per publish. That is what this
    /// makes possible; without it every new scenario is another 25 s whether or not it needed
    /// its own program.</para>
    ///
    /// <para>Deliberately NOT <c>_dir</c>-scoped: <c>[SetUp]</c>/<c>[TearDown]</c> give each
    /// test a fresh directory and delete it, which is right for tests that assert on
    /// <c>obj/gen</c> state and fatal to a shared artifact. These live in their own directories,
    /// cleaned in <see cref="OneTimeTearDown"/>. Tests that must observe a build HAPPENING —
    /// the cold/warm cache pair above all — keep using <see cref="Build"/>.</para>
    /// </summary>
    private static SharedBuild BuildOnce(
        string projectName, IReadOnlyDictionary<string, string> files, string itemGroupXml = "")
    {
        var key = projectName + " " + itemGroupXml + " "
                  + string.Join(" ", files.OrderBy(f => f.Key, StringComparer.Ordinal)
                      .Select(f => f.Key + "" + f.Value));

        return SharedBuilds.GetOrAdd(key, _ => new Lazy<SharedBuild>(() =>
        {
            var dir = NetShimPipelineFixture.NewTempDir("blnet-shared5-");
            SharedDirs.Add(dir);
            foreach (var file in files)
                File.WriteAllText(Path.Combine(dir, file.Key), file.Value);

            var projectPath = NetShimPipelineFixture.WriteProject(dir, projectName, itemGroupXml);
            var stopwatch = Stopwatch.StartNew();
            var result = CppProjectBuilder.Build(ProjectFile.Load(projectPath), "Release");
            stopwatch.Stop();
            TestContext.Out.WriteLine(
                $"{projectName}: shared build took {stopwatch.Elapsed.TotalSeconds:F1}s");
            foreach (var message in result.Messages)
                TestContext.Out.WriteLine("  " + message);
            return new SharedBuild(dir, result, stopwatch.Elapsed);
        })).Value;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        foreach (var dir in SharedDirs)
            NetShimPipelineFixture.TryDeleteDir(dir);
    }

    private static void AssertBuilt(CppProjectBuildResult result, string label)
    {
        Assert.That(result.Success, Is.True,
            label + " did not build.\n" + NetShimPipelineFixture.Diagnostics(result)
            + "\n" + result.RawToolchainOutput);
        Assert.That(File.Exists(result.ExecutablePath), Is.True, result.ExecutablePath);
    }

    // =====================================================================================
    // THE MILESTONE.
    // =====================================================================================

    /// <summary>
    /// <b>The first real .NET call from a natively-compiled BasicLang program.</b> Everything the
    /// P2a-2 flip is for, in one program: a constructor crossing the boundary and handing back a
    /// handle, that handle flowing into an instance call as a receiver, a String argument crossing
    /// as UTF-8, and a Boolean result crossing back — all through a shim this build generated,
    /// published with Native AOT, cached and deployed next to the executable.
    ///
    /// <para>The second line is not decoration: <c>IsMatch("bbb")</c> must be FALSE, which is what
    /// separates "the boundary works" from "the boundary returns a constant".</para>
    /// </summary>
    /// <summary>
    /// THE milestone program. Shared through <c>BuildOnce</c> so later scenarios can assert on
    /// it without paying a second publish — the "fewer, richer programs" lever §10.2's key
    /// leaves as the only one.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> MilestoneSources =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Program.bas"] = """
                Using System.Text.RegularExpressions

                Module Program
                 Sub Main()
                  Dim Rx As New Regex("^a+$")
                  If Rx.IsMatch("aaa") Then
                   Console.WriteLine("True")
                  Else
                   Console.WriteLine("False")
                  End If
                  Console.WriteLine(Rx.IsMatch("bbb"))
                 End Sub
                End Module
                """,
        };

    [Test]
    public void Milestone_ANativeProgramCallsDotNet_AndPrintsTheResult()
    {
        var built = BuildOnce("Milestone", MilestoneSources);
        var result = built.Result;
        var elapsed = built.Elapsed;
        AssertBuilt(result, "the milestone program");

        var shim = Path.Combine(
            Path.GetDirectoryName(result.ExecutablePath)!,
            NetProxyEmitter.ShimModuleFileName(NetShimGenerator.ShimAssemblyName("Milestone")));
        Assert.That(File.Exists(shim), Is.True,
            "the published shim was not deployed next to the exe (§10.4, phase 7). "
            + "blnet_startup.g.cpp loads it by BARE NAME and the OS resolves that from the "
            + "executable's own directory, so without this copy the program dies at startup.");

        Assert.That(NetShimPipelineFixture.Run(result.ExecutablePath!), Is.EqualTo("True\n0\n"),
            "'True' proves the constructor, the receiver handle, the UTF-8 argument and the "
            + "Boolean result all crossed correctly; '0' is IsMatch(\"bbb\") = False rendered by "
            + "the pre-existing C++ Boolean-formatting gap (gaps spec item 8) — if THAT line "
            + "changed to 'False', the gap was fixed and this expectation should follow it. If the "
            + "first line is 'False', the .NET call ran and answered wrong; if the program failed "
            + "to start, look at the shim deployment and the §9.3 handshake.");

        TestContext.Out.WriteLine($"MILESTONE wall time (cold, incl. C++ link): {elapsed.TotalSeconds:F1}s");
    }

    // =====================================================================================
    // §10.2 — cold, then warm.
    // =====================================================================================

    /// <summary>
    /// §10.2: "Hit → skip entirely. The ordinary edit-run loop pays nothing." The second build of
    /// an unchanged project must find the manifest, validate it, and not publish at all.
    ///
    /// <para>Asserted three ways, because each alone is weak: the manifest must EXIST after the
    /// cold build (a publish that never committed would silently re-publish forever), the message
    /// channel must switch from "Publishing" to "up to date" (deterministic), and the warm build
    /// must actually be faster (which is what the cache is FOR — a hit that still paid the publish
    /// would satisfy the first two).</para>
    /// </summary>
    [Test]
    public void SecondIdenticalBuild_HitsTheCacheAndSkipsPhaseFive()
    {
        Write("Program.bas", """
            Using System.Text.RegularExpressions

            Module Program
             Sub Main()
              Dim Rx As New Regex("^a+$")
              If Rx.IsMatch("aaa") Then
               Console.WriteLine("hit")
              End If
             End Sub
            End Module
            """);

        var (cold, coldElapsed) = Build("WarmProbe");
        AssertBuilt(cold, "the cold build");

        var manifest = Path.Combine(
            NetShimCache.CacheRoot(_dir), "Release", "shim-cache.txt");
        Assert.That(File.Exists(manifest), Is.True,
            "the cold build published but never committed a cache manifest, so every subsequent "
            + "build would re-publish. NetShimCache.Commit is refused for a null key — which is "
            + "what happens when any environment field is blank (no dotnet on PATH, an unreadable "
            + "reference). Manifest path: " + manifest);

        var (warm, warmElapsed) = Build("WarmProbe");
        AssertBuilt(warm, "the warm build");

        Assert.Multiple(() =>
        {
            Assert.That(cold.Messages.Any(m => m.Contains("Publishing .NET shim")), Is.True,
                "the COLD build did not publish, so this test is not measuring a cache miss "
                + "followed by a hit at all.");
            Assert.That(warm.Messages.Any(m => m.Contains("Publishing .NET shim")), Is.False,
                "the warm build published again. TryGetHit rejected the manifest the cold build "
                + "just wrote — the usual cause is a key component that is not stable across "
                + "processes (§10.2 exists because string.GetHashCode is randomized per process).");
            Assert.That(warm.Messages.Any(m => m.Contains("up to date")), Is.True,
                "the warm build neither published nor reported a hit, which means phase 5 was "
                + "skipped for some other reason — check that the surface is still non-empty.");
            Assert.That(warmElapsed, Is.LessThan(coldElapsed),
                $"the warm build ({warmElapsed.TotalSeconds:F1}s) was not faster than the cold one "
                + $"({coldElapsed.TotalSeconds:F1}s). A hit that still pays the publish is not a hit.");
        });

        Assert.That(NetShimPipelineFixture.Run(warm.ExecutablePath!), Is.EqualTo("hit\n"),
            "the cache-hit build must produce a WORKING program — the reused DLL still has to be "
            + "deployed next to the exe, which is the step a 'skip entirely' fast path is most "
            + "likely to skip too.");
    }

    // =====================================================================================
    // §11.1 — the Task-7a handoff, proven on a REAL shim.
    // =====================================================================================

    /// <summary>
    /// <b>The behavioural proof that the generated shim's <c>Fail(ex)</c> sends the full
    /// inheritance chain.</b> <c>New Regex("[")</c> throws <c>RegexParseException</c>, which
    /// derives from <c>ArgumentException</c>; the program catches <c>Exception</c>. With the
    /// pre-7b single-name last-error field the chain is
    /// <c>"System.Text.RegularExpressions.RegexParseException"</c>, <c>NetException.Matches</c>
    /// answers false for every clause, the ladder falls through to its bare <c>throw;</c> and the
    /// exception escapes <c>Main</c> — so this test fails by the program CRASHING, not by printing
    /// the wrong thing.
    ///
    /// <para>The 7a stub-runtime tests planted the chain by hand and could never have caught this;
    /// that is precisely why the plan flagged it for verification here.</para>
    /// </summary>
    [Test]
    public void TypedCatchOfABaseType_MatchesTheGeneratedShimsChain()
    {
        Write("Program.bas", """
            Using System.Text.RegularExpressions

            Module Program
             Sub Main()
              Try
               Dim Bad As New Regex("[")
               Console.WriteLine("NOT REACHED")
              Catch ex As Exception
               Console.WriteLine("CAUGHT")
              End Try
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        var (result, _) = Build("ChainProbe");
        AssertBuilt(result, "the typed-catch program");

        Assert.That(NetShimPipelineFixture.Run(result.ExecutablePath!),
            Is.EqualTo("CAUGHT\ndone\n"),
            "a managed RegexParseException must be caught by `Catch ex As Exception`. §11.1 makes "
            + "the last-error TYPE field the ';'-separated inheritance chain, most-derived first, "
            + "and NetCheckTyped matches ELEMENTS of it — with only ex.GetType().FullName the "
            + "clause never matches and the exception escapes Main entirely.");
    }

    // =====================================================================================
    // §11.3 + §15.10 — BL6020 at tier 1, on real ILC output.
    // =====================================================================================

    /// <summary>
    /// The provenance proof, end to end: a BasicLang call to a <c>[RequiresDynamicCode]</c> member
    /// makes ILC report IL3050 against the GENERATED wrapper, and the provenance map lifts it back
    /// onto the <c>.bas</c> line the user wrote. §15.10 makes it an ERROR, so the build fails
    /// rather than shipping a call that is known to throw.
    ///
    /// <para>The member is reached by a CALL rather than by a <c>&lt;NetProxy&gt;</c> on purpose:
    /// the §7.2 filter BL6026-omits AOT-hostile members from a declared surface, so a declared one
    /// could never reach the shim — and only a call site has a <c>.bas</c> line to attribute
    /// to.</para>
    /// </summary>
    [Test]
    public void AotHostileMemberReachedFromBasicLang_IsABl6020ErrorAtTheBasLine()
    {
        var probe = NetShimPipelineFixture.EmitProbeAssembly(_dir);
        Write("Program.bas", """
            Using Aot.Probe

            Module Program
             Sub Main()
              Console.WriteLine(AotProbe.Risky(1))
             End Sub
            End Module
            """);

        var (result, _) = Build("AotCeiling", NetShimPipelineFixture.ReferenceItemGroup(probe));

        var bl6020 = result.Diagnostics.Where(d => d.Code == "BL6020").ToList();
        var errors = bl6020.Where(d => !d.IsWarning).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(bl6020, Is.Not.Empty,
                "ILC reported nothing this mapper recognized. Check that the shim csproj still "
                + "carries IsAotCompatible=true and TrimmerSingleWarn=false — with single-warn on, "
                + "every per-member warning collapses into one assembly-level line carrying no "
                + "member name, and tier 1 becomes unreachable.\n"
                + NetShimPipelineFixture.Diagnostics(result));
            Assert.That(errors, Is.Not.Empty,
                "IL3050 is RequiresDynamicCode and §15.10 pins it to ERROR — the call throws at "
                + "runtime, so building would ship a known crash.");
            Assert.That(result.Success, Is.False, "a BL6020 error must fail the build.");

            var tierOne = errors.FirstOrDefault(
                d => d.FilePath != null && d.FilePath.EndsWith("Program.bas", StringComparison.Ordinal));
            Assert.That(tierOne, Is.Not.Null,
                "no BL6020 was attributed to the .bas call site. This is §11.3 tier 1 and the whole "
                + "reason the provenance map exists: without it the user is shown a line number in "
                + "obj/gen/shim/Exports.g.cs, a file they never wrote. Diagnostics were:\n"
                + NetShimPipelineFixture.Diagnostics(result));
            Assert.That(tierOne!.Line, Is.EqualTo(5),
                "the attributed line must be the CALL, not the file. (Line 5 is the "
                + "Console.WriteLine(AotProbe.Risky(1)) line of the program above.)");
            Assert.That(tierOne.Message, Does.Contain("IL3050"),
                "the IL code must survive — it is the only token the user can search for.");
            Assert.That(tierOne.Message, Does.Contain(AotDiagnosticMapper.Remedy));
        });
    }

    // =====================================================================================
    // §12.5 — the zero-.bas smoke.
    // =====================================================================================

    /// <summary>
    /// A project with NO BasicLang source at all — a hand-written C++ <c>main()</c> plus a
    /// <c>&lt;NetProxy&gt;</c> — publishes a shim and links the startup translation unit. §9.5's
    /// executable-shape rule says this must work: the static-initializer object in
    /// <c>blnet_startup.g.cpp</c> covers both executable shapes.
    ///
    /// <para>Spec §17 letters this test before the flip, but its operative precondition (§9.5's
    /// gate rework) landed in P2a-1 and the "shim actually initializes" half is only satisfiable
    /// now — hence a SMOKE here; the full §12.5 assertion is Task 14's.</para>
    /// </summary>
    [Test]
    public void ZeroBasProjectWithANetProxy_PublishesAndLinksTheStartupTu()
    {
        var probe = NetShimPipelineFixture.EmitProbeAssembly(_dir);
        Write("main.cpp", """
            #include <cstdio>
            int main() { std::printf("native ok\n"); return 0; }
            """);

        var (result, _) = Build("ZeroBasProbe",
            NetShimPipelineFixture.ReferenceItemGroup(probe)
            + "\n  <ItemGroup>\n    <NetProxy Include=\"Aot.Probe.Widget\" />\n  </ItemGroup>");

        AssertBuilt(result, "the zero-.bas <NetProxy> project");

        var outputDir = Path.GetDirectoryName(result.ExecutablePath)!;
        Assert.Multiple(() =>
        {
            Assert.That(
                File.Exists(Path.Combine(_dir, "obj", "gen", NetProxyEmitter.StartupFileName)),
                Is.True,
                "blnet_startup.g.cpp was not emitted for a project whose only .NET is a "
                + "<NetProxy> declaration.");
            Assert.That(File.Exists(Path.Combine(outputDir,
                    NetProxyEmitter.ShimModuleFileName(
                        NetShimGenerator.ShimAssemblyName("ZeroBasProbe")))),
                Is.True,
                "the shim was published but not deployed. A pure-C++ project reaches phase 7 the "
                + "same way a BasicLang one does.");
        });

        // Running it is the part that proves the startup TU was actually LINKED: the
        // static-initializer object runs blnet_startup() before main(), and a failed §9.3
        // handshake exits with NetProxyEmitter.StartupFailureExitCode rather than printing.
        Assert.That(NetShimPipelineFixture.Run(result.ExecutablePath!), Is.EqualTo("native ok\n"),
            "the program did not reach its own main(). The static-initializer object in "
            + "blnet_startup.g.cpp runs the §9.3 handshake first — reaching main() means the shim "
            + "loaded, the ABI matched and every proxy slot bound.");
    }
}
