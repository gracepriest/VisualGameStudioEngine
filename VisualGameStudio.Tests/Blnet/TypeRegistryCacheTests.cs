using BasicLang.Compiler.SemanticAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Pins two SDK/type-index correctness rules the LSP startup path depends on.
///
/// <para><b>1. <see cref="TypeRegistry.LoadIndexFromCache"/> must reject a degenerate cache.</b>
/// Both consumers (<c>DocumentManager.InitializeTypeRegistry</c>,
/// <c>WorkspaceManager.InitializeTypeRegistry</c>) skip <c>BuildIndex</c> — and with it SDK
/// detection — whenever the load returns <c>true</c>. The shipped shape returned <c>true</c> for
/// ANY parseable file: a zero-byte file, a file of junk lines (no <c>|</c> means every line is
/// silently skipped), or a stale index whose assembly paths no longer exist. Measured incident
/// (see <c>NetTypeResolverTests.cs</c>, the <c>NewRegistry</c> remarks): a one-namespace index of
/// dead paths passed the load and IntelliSense ran on a dead index for the life of the session.</para>
///
/// <para><b>2. SDK version directories must be ordered by VERSION, not by string.</b>
/// <c>"9.0.12"</c> outranks <c>"10.0.0"</c> ordinally, and <c>"net9.0"</c> outranks
/// <c>"net10.0"</c> — so the shipped <c>OrderByDescending(d =&gt; d)</c> could never select .NET 10
/// once installed. The oracle in these tests is semantic version ordering, deliberately independent
/// of however <see cref="TypeRegistry.PickNewestVersionDirectory"/> keys its sort.</para>
///
/// <para><b>Never call <c>new TypeRegistry()</c> here.</b> The parameterless constructor points at
/// the ONE user-scope file <c>%LOCALAPPDATA%\BasicLang\namespace_index.json</c>, and
/// <c>BuildIndex</c> CLEARS the index and REPLACES that file — a fixture that defaults it destroys
/// the developer's real LSP cache (measured: it happened). Every registry in this fixture goes
/// through <see cref="NewRegistry"/>, which confines the cache to this test's temp directory.</para>
/// </summary>
[TestFixture]
public class TypeRegistryCacheTests
{
    private string _dir = null!;
    private string _cachePath = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "typeregcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _cachePath = Path.Combine(_dir, "namespace_index.json");
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

    /// <summary>A registry whose namespace-index cache lives in THIS test's temp directory.</summary>
    private TypeRegistry NewRegistry() => new TypeRegistry(_cachePath);

    /// <summary>
    /// Compiles a real on-disk assembly into the temp directory — same shape as
    /// <c>NetTypeResolverTests.EmitFreshAssembly</c>, and for the same reason: a fresh identity is
    /// unambiguous evidence that the registry read THIS file.
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

    // ------------------------------------------------------------------
    // Cache validity: a load that succeeds must mean there is a usable index.
    // ------------------------------------------------------------------

    [Test]
    public void AnEmptyCacheFileIsRejected()
    {
        File.WriteAllBytes(_cachePath, Array.Empty<byte>());

        Assert.That(NewRegistry().LoadIndexFromCache(), Is.False,
            "a zero-byte cache has no entries to offer; returning true here makes both LSP " +
            "consumers skip BuildIndex and run on an empty index.");
    }

    [Test]
    public void AJunkCacheFileWithNoSeparatorsIsRejected()
    {
        File.WriteAllLines(_cachePath, new[] { "this is not an index", "neither is this", "" });

        Assert.That(NewRegistry().LoadIndexFromCache(), Is.False,
            "every line without a '|' is silently skipped by the parser, so this file contributes " +
            "ZERO entries — a load that parsed nothing must not report success.");
    }

    [Test]
    public void ACacheWhoseAssemblyPathsAreAllDeadIsRejected()
    {
        // The real incident shape: a well-formed index naming only assemblies that no longer exist
        // (a temp-dir build cached to the user-scope file, temp dir deleted in TearDown).
        var dead1 = Path.Combine(_dir, "Gone.dll");
        var dead2 = Path.Combine(_dir, "AlsoGone.dll");
        File.WriteAllLines(_cachePath, new[]
        {
            $"Contoso.Stale|{dead1};{dead2}",
            $"Contoso.Stale.Child|{dead2}"
        });

        Assert.That(NewRegistry().LoadIndexFromCache(), Is.False,
            "an index whose every assembly path is dead cannot answer a single LoadNamespace; " +
            "accepting it is exactly the measured incident — IntelliSense on a dead index.");
    }

    [Test]
    public void ACacheWithOneLiveAssemblyPathLoadsAndItsNamespacesAreVisible()
    {
        var live = typeof(TypeRegistryCacheTests).Assembly.Location;
        Assert.That(File.Exists(live), Is.True, "guard: the test assembly's own file must exist");

        var dead = Path.Combine(_dir, "Gone.dll");
        File.WriteAllLines(_cachePath, new[]
        {
            $"Contoso.CacheAlive|{live}",
            $"Contoso.CacheStale|{dead}"
        });

        var registry = NewRegistry();
        Assert.That(registry.LoadIndexFromCache(), Is.True,
            "one live assembly path is enough — validity is a cheap liveness probe, not a full " +
            "audit, so a healthy cache pays ~one File.Exists.");
        Assert.That(registry.LoadNamespace("Contoso.CacheAlive"), Is.True,
            "the accepted index must actually be merged: its namespaces have to be visible " +
            "through the same lookup the LSP's Using-statement path takes.");
    }

    [Test]
    public void ARejectedCacheLeavesNoPartialStateBehind()
    {
        File.WriteAllLines(_cachePath, new[] { $"Contoso.DeadCache|{Path.Combine(_dir, "Gone.dll")}" });

        var registry = NewRegistry();
        Assert.That(registry.LoadIndexFromCache(), Is.False,
            "guard: the dead-path cache must be rejected before the pollution check means anything");
        Assert.That(registry.LoadNamespace("Contoso.DeadCache"), Is.False,
            "a rejected load must leave the in-memory index EXACTLY as it was. The old shape " +
            "merged into _namespaceIndex WHILE parsing, so the dead namespace stayed visible even " +
            "on the paths that did return false.");

        // And the registry is still fully usable afterwards: a rebuild over a real search path
        // indexes fresh work, proving no wedged partial state.
        var name = "TypeRegCacheProbe" + Guid.NewGuid().ToString("N");
        EmitFreshAssembly(name, "namespace Contoso.CacheProbe { public class Seen { } }");
        registry.AddSearchPath(_dir);
        registry.BuildIndex();

        Assert.That(registry.LoadNamespace("Contoso.CacheProbe"), Is.True,
            "BuildIndex after a rejected load must index normally");
    }

    // ------------------------------------------------------------------
    // Version-directory selection: semantic version order, never string order.
    // ------------------------------------------------------------------

    [Test]
    public void PicksDotNet10OverDotNet9AmongRefPackVersionDirectories()
    {
        var packRoot = Path.Combine("C:", "Program Files", "dotnet", "packs", "Microsoft.NETCore.App.Ref");
        var nine = Path.Combine(packRoot, "9.0.12");
        var ten = Path.Combine(packRoot, "10.0.0");

        Assert.That(TypeRegistry.PickNewestVersionDirectory(new[] { nine, ten }), Is.EqualTo(ten),
            "the exact shipped bug: '9.0.12' outranks '10.0.0' as a string, so a string sort " +
            "never hands the LSP .NET 10 once it is installed.");
    }

    [Test]
    public void PicksNet10OverNet9AmongRefMonikerDirectories()
    {
        Assert.That(TypeRegistry.PickNewestVersionDirectory(new[] { "net8.0", "net9.0", "net10.0" }),
            Is.EqualTo("net10.0"),
            "'net9.0' outranks 'net10.0' as a string; the moniker's 'net' prefix must be " +
            "stripped and the remainder compared as a version.");
    }

    [Test]
    public void APreviewSuffixStillKeysAsItsNumericPrefix()
    {
        var nine = Path.Combine("packs", "9.0.12");
        var tenPreview = Path.Combine("packs", "10.0.0-preview.7.25380.108");

        Assert.That(TypeRegistry.PickNewestVersionDirectory(new[] { nine, tenPreview }),
            Is.EqualTo(tenPreview),
            "'10.0.0-preview.7.25380.108' must key as 10.0.0 — Version.TryParse rejects the raw " +
            "name, and dropping it would hide a preview-only SDK entirely.");
    }

    [Test]
    public void UnparseableNamesLoseToParseableOnes()
    {
        Assert.That(TypeRegistry.PickNewestVersionDirectory(new[] { "zzz-not-a-version", "net8.0" }),
            Is.EqualTo("net8.0"),
            "'zzz' outranks 'net8.0' ordinally; anything that parses as a version must outrank " +
            "everything that does not.");
    }

    [Test]
    public void DegenerateInputsAreHandledDeterministically()
    {
        var names = new[] { "alpha", "beta", "gamma" };
        var forward = TypeRegistry.PickNewestVersionDirectory(names);
        var reversed = TypeRegistry.PickNewestVersionDirectory(names.Reverse());

        Assert.That(names, Does.Contain(forward),
            "all-unparseable input must still return one of its inputs, not throw or invent");
        Assert.That(forward, Is.EqualTo(reversed),
            "…and the pick must not depend on enumeration order");
        Assert.That(TypeRegistry.PickNewestVersionDirectory(Enumerable.Empty<string>()), Is.Null,
            "empty input has no answer");
    }
}
