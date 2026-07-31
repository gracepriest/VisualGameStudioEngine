using System.Security.Cryptography;
using System.Text;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.Net;
using BasicLang.Net;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// <see cref="NetShimCache"/> — spec §10.2's content-hash cache for the AOT shim publish.
///
/// <para><b>Every test here exists because a false HIT is the worst failure this design has.</b>
/// A warm publish costs ~11 s (Task 1's measurement), so a false MISS costs eleven seconds; a
/// false hit ships a native executable bound to a shim whose exports no longer match, with no
/// diagnostic anywhere. The asymmetry is why the fixture spends most of its assertions proving
/// that things which SHOULD miss do miss.</para>
///
/// <para><b>The MVID fixture.</b> <see cref="EmitProbeAssembly"/> emits the same source twice
/// with <c>Deterministic</c> left at its default (false), so the two assemblies are guaranteed
/// byte-identical in LENGTH and differ in MVID. The pair is then written to the SAME path, so
/// the only thing that can move the key is the file's content — a fixture whose two inputs sat
/// at different paths would prove nothing, since the path is in the key too.
/// <see cref="KeyUsesMvidNotTimestampAndSize"/> asserts that precondition before it asserts the
/// conclusion, so the test fails loudly rather than silently degenerating if Roslyn ever stops
/// producing that shape.</para>
/// </summary>
[TestFixture]
public class NetShimCacheTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "blnet-cache-" + Guid.NewGuid().ToString("N"));
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

    // ------------------------------------------------------------------
    // Fixture builders.
    // ------------------------------------------------------------------

    private const string Config = "Release";

    private static NetShimCacheEnvironment Env(
        string configuration = Config, string tfm = "net8.0", string rid = "win-x64",
        string toolchain = "clang++ 20.1.0", string sdk = "8.0.404") =>
        new(configuration, tfm, rid, toolchain, sdk);

    private static NetParameterDescriptor P(string type, NetRefKind refKind = NetRefKind.None) =>
        new(refKind, type);

    private static NetMemberDescriptor Member(
        string name, string declaringType, params NetParameterDescriptor[] parameters) =>
        new(name, declaringType, NetMemberCategory.Method, false, 0, "System.String", parameters);

    /// <summary>Built per call — <see cref="NetSurface"/>'s <c>==</c> is reference equality.</summary>
    private static NetSurface Surface() => new(
        new[] { Member("Match", "System.Text.RegularExpressions.Regex", P("System.String")) },
        Array.Empty<string>());

    /// <summary>
    /// Emits <paramref name="source"/> to <paramref name="path"/>, overwriting. Non-deterministic
    /// on purpose (see the fixture's remarks): two emits of the same source give two different
    /// MVIDs at identical length.
    /// </summary>
    private static void EmitProbeAssembly(string path, string source)
    {
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(path),
            new[] { CSharpSyntaxTree.ParseText(source) },
            NetTypeResolverTestRefs.FrameworkPaths.Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = compilation.Emit(path);
        Assert.That(result.Success, Is.True,
            "the FIXTURE failed to build its probe assembly (not the cache): " + string.Join("\n",
                result.Diagnostics.Where(d =>
                    d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)));
    }

    private static string Sha256Hex(string text)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var sb = new StringBuilder(digest.Length * 2);
        foreach (var b in digest) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private string WriteDll(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ==================================================================
    // Component 1 — reference identities are MVIDs.
    // ==================================================================

    [Test]
    public void KeyUsesMvidNotTimestampAndSize()
    {
        var asm = Path.Combine(_dir, "Probe.dll");
        const string source = "public class C { public const string S = \"AAAA\"; }";

        EmitProbeAssembly(asm, source);
        var lengthA = new FileInfo(asm).Length;
        var stamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(asm, stamp);
        var mvidA = NetShimCache.TryReadMvid(asm);
        var keyA = NetShimCache.KeyFor(Surface(), new[] { asm }, Env());

        EmitProbeAssembly(asm, source);          // same path, same source, fresh MVID
        var lengthB = new FileInfo(asm).Length;
        File.SetLastWriteTimeUtc(asm, stamp);
        var mvidB = NetShimCache.TryReadMvid(asm);
        var keyB = NetShimCache.KeyFor(Surface(), new[] { asm }, Env());

        // PRECONDITIONS. Without these the conclusion below is vacuous: a pair that differs in
        // length or stamp would key differently even under a stat-based implementation.
        Assert.Multiple(() =>
        {
            Assert.That(lengthB, Is.EqualTo(lengthA),
                "FIXTURE precondition lost: the two probe assemblies must have IDENTICAL length, "
                + "otherwise this test cannot distinguish an MVID-keyed cache from a size-keyed "
                + "one. Fix the fixture (EmitProbeAssembly), not NetShimCache.");
            Assert.That(File.GetLastWriteTimeUtc(asm), Is.EqualTo(stamp),
                "FIXTURE precondition lost: both probes must carry the same last-write stamp. "
                + "Fix the fixture, not NetShimCache.");
            Assert.That(mvidA, Is.Not.Null.And.Not.EqualTo(mvidB),
                "FIXTURE precondition lost: two non-deterministic Roslyn emits must produce two "
                + "different MVIDs. Fix the fixture (check CSharpCompilationOptions.Deterministic "
                + "has not been turned on), not NetShimCache.");
        });

        Assert.That(keyA, Is.Not.Null, "guard: a fully-specified key must exist to compare");
        Assert.That(keyA, Is.Not.EqualTo(keyB),
            "Spec §10.2 requires MVID. Timestamp+size collides on a rebuilt assembly whose content "
            + "changed — a false cache HIT ships a stale shim, which is the worst failure mode "
            + "here. Fix NetShimCache.KeyFor's reference component; the fixture asserted above "
            + "that the two inputs really do share a length and a timestamp.");
    }

    [Test]
    public void KeyIgnoresTheTimestampOfAReference()
    {
        var asm = Path.Combine(_dir, "Probe.dll");
        EmitProbeAssembly(asm, "public class C { }");

        var before = NetShimCache.KeyFor(Surface(), new[] { asm }, Env());
        File.SetLastWriteTimeUtc(asm, DateTime.UtcNow.AddDays(1));
        var after = NetShimCache.KeyFor(Surface(), new[] { asm }, Env());

        Assert.That(after, Is.EqualTo(before),
            "The key must not move when a reference's timestamp moves and its CONTENT does not — "
            + "a touched-but-unchanged assembly (git checkout, msbuild copy) would otherwise cost "
            + "an 11 s republish on every build. Fix NetShimCache.KeyFor: it is reading file "
            + "metadata, not the MVID.");
    }

    [Test]
    public void KeyIgnoresAFileOutsideTheReferenceClosure()
    {
        var asm = Path.Combine(_dir, "Probe.dll");
        EmitProbeAssembly(asm, "public class C { }");
        var before = NetShimCache.KeyFor(Surface(), new[] { asm }, Env());

        var unrelated = Path.Combine(_dir, "Unrelated.dll");
        EmitProbeAssembly(unrelated, "public class D { }");
        var after = NetShimCache.KeyFor(Surface(), new[] { asm }, Env());

        Assert.That(after, Is.EqualTo(before),
            "Only the assemblies in the reference closure may affect the key. Fix "
            + "NetShimCache.KeyFor: something is hashing the directory rather than the list it "
            + "was handed.");
    }

    [Test]
    public void KeyChangesWhenAReferenceIsAdded()
    {
        var a = Path.Combine(_dir, "A.dll");
        var b = Path.Combine(_dir, "B.dll");
        EmitProbeAssembly(a, "public class A { }");
        EmitProbeAssembly(b, "public class B { }");

        Assert.That(NetShimCache.KeyFor(Surface(), new[] { a, b }, Env()),
            Is.Not.EqualTo(NetShimCache.KeyFor(Surface(), new[] { a }, Env())),
            "A second reference changes what the generated csproj compiles against. Fix "
            + "NetShimCache.KeyFor's reference component.");
    }

    [Test]
    public void KeyChangesWhenAReferenceMovesToADifferentPath()
    {
        var a = Path.Combine(_dir, "A.dll");
        EmitProbeAssembly(a, "public class A { }");
        var moved = Path.Combine(_dir, "sub");
        Directory.CreateDirectory(moved);
        var b = Path.Combine(moved, "A.dll");
        File.Copy(a, b);

        Assert.That(NetShimCache.KeyFor(Surface(), new[] { b }, Env()),
            Is.Not.EqualTo(NetShimCache.KeyFor(Surface(), new[] { a }, Env())),
            "The path is what the generated csproj's <HintPath> carries, so the same content at a "
            + "different location is a different shim project. Fix NetShimCache.KeyFor: it is "
            + "hashing the MVID alone and dropping the path.");
    }

    [Test]
    public void AMissingReferenceMakesTheKeyNull()
    {
        var missing = Path.Combine(_dir, "NotThere.dll");

        Assert.That(NetShimCache.KeyFor(Surface(), new[] { missing }, Env()), Is.Null,
            "A reference whose MVID cannot be read is an input we cannot identify, and an "
            + "unidentifiable input must be a MISS, never a key that happens to be stable. Fix "
            + "NetShimCache.KeyFor.");
    }

    [Test]
    public void AnUnreadableReferenceMakesTheKeyNull()
    {
        var garbage = WriteDll("Garbage.dll", "this is not a PE image");

        Assert.That(NetShimCache.KeyFor(Surface(), new[] { garbage }, Env()), Is.Null,
            "A file that is not a managed assembly (a native DLL, a truncated copy) must make the "
            + "key null. Fix NetShimCache.KeyFor / TryReadMvid.");
    }

    // ==================================================================
    // TryReadMvid — the no-lock claim.
    // ==================================================================

    [Test]
    public void ReadingAnMvidTakesNoLockOnTheFile()
    {
        var asm = Path.Combine(_dir, "Probe.dll");
        EmitProbeAssembly(asm, "public class C { }");

        var mvid = NetShimCache.TryReadMvid(asm);
        Assert.That(mvid, Is.Not.Null.And.Length.EqualTo(32), "guard: the MVID must be readable");

        // If TryReadMvid loaded the assembly (Assembly.LoadFrom, banned by Task 7) or left a
        // handle open, BOTH of these throw IOException on Windows.
        Assert.DoesNotThrow(() => File.WriteAllBytes(asm, new byte[] { 1, 2, 3 }),
            "NetShimCache.TryReadMvid left the file locked — it must read metadata through "
            + "PEReader over a FileShare.ReadWrite|Delete stream, never load the assembly.");
        Assert.DoesNotThrow(() => File.Delete(asm),
            "NetShimCache.TryReadMvid left the file locked against deletion.");
    }

    [Test]
    public void TryReadMvidAnswersNullRatherThanThrowing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NetShimCache.TryReadMvid(null), Is.Null);
            Assert.That(NetShimCache.TryReadMvid(""), Is.Null);
            Assert.That(NetShimCache.TryReadMvid(Path.Combine(_dir, "nope.dll")), Is.Null);
            Assert.That(NetShimCache.TryReadMvid(WriteDll("bad.dll", "MZ but not really")), Is.Null);
            Assert.That(NetShimCache.TryReadMvid(_dir), Is.Null, "a directory, not a file");
        });
    }

    // ==================================================================
    // Component 2 — the mangled member set.
    // ==================================================================

    [Test]
    public void KeyChangesWhenTheMangledMemberSetChanges()
    {
        var asm = Path.Combine(_dir, "Probe.dll");
        EmitProbeAssembly(asm, "public class C { }");

        var one = Surface();
        var two = new NetSurface(
            new[]
            {
                Member("Match", "System.Text.RegularExpressions.Regex", P("System.String")),
                Member("IsMatch", "System.Text.RegularExpressions.Regex", P("System.String")),
            },
            Array.Empty<string>());

        Assert.That(NetShimCache.KeyFor(two, new[] { asm }, Env()),
            Is.Not.EqualTo(NetShimCache.KeyFor(one, new[] { asm }, Env())),
            "A different member set means a different export set and a different shim. Fix "
            + "NetShimCache.KeyFor's member component (spec §10.2).");
    }

    [Test]
    public void KeyChangesWhenAMemberSignatureChangesButItsNameDoesNot()
    {
        var asm = Path.Combine(_dir, "Probe.dll");
        EmitProbeAssembly(asm, "public class C { }");

        var stringArg = Surface();
        var intArg = new NetSurface(
            new[] { Member("Match", "System.Text.RegularExpressions.Regex", P("System.Int32")) },
            Array.Empty<string>());

        Assert.That(NetShimCache.KeyFor(intArg, new[] { asm }, Env()),
            Is.Not.EqualTo(NetShimCache.KeyFor(stringArg, new[] { asm }, Env())),
            "Two overloads mangle to two different export names and produce two different wire "
            + "signatures. Fix NetShimCache.KeyFor: it is hashing member NAMES rather than "
            + "NetNameMangler's output.");
    }

    [Test]
    public void KeyIsUnchangedByADuplicateMember()
    {
        var asm = Path.Combine(_dir, "Probe.dll");
        EmitProbeAssembly(asm, "public class C { }");

        var once = Surface();
        var twice = new NetSurface(
            new[]
            {
                Member("Match", "System.Text.RegularExpressions.Regex", P("System.String")),
                Member("Match", "System.Text.RegularExpressions.Regex", P("System.String")),
            },
            Array.Empty<string>());

        Assert.That(NetShimCache.KeyFor(twice, new[] { asm }, Env()),
            Is.EqualTo(NetShimCache.KeyFor(once, new[] { asm }, Env())),
            "§7.1's collector walks CALL SITES, so one member arrives once per call site. Both "
            + "NetShimGenerator and NetProxyEmitter collapse duplicates on the mangled name and "
            + "emit identical output either way — the key must collapse on the same key, or "
            + "adding a second call to the same method costs an 11 s republish. Fix "
            + "NetShimCache.KeyFor's member component.");
    }

    [Test]
    public void KeyChangesWhenADeclaredTypeIsAdded()
    {
        var asm = Path.Combine(_dir, "Probe.dll");
        EmitProbeAssembly(asm, "public class C { }");

        var declared = new NetSurface(Surface().Members, new[] { "System.IO.Stream" });

        Assert.That(NetShimCache.KeyFor(declared, new[] { asm }, Env()),
            Is.Not.EqualTo(NetShimCache.KeyFor(Surface(), new[] { asm }, Env())),
            "<NetProxy> declarations are part of the surface (NetSurface.DeclaredTypeNames) and "
            + "reach BL6022/BL6026. Fix NetShimCache.KeyFor's member component.");
    }

    [Test]
    public void AnEmptySurfaceHasNoKey()
    {
        var asm = Path.Combine(_dir, "Probe.dll");
        EmitProbeAssembly(asm, "public class C { }");

        Assert.That(NetShimCache.KeyFor(NetSurface.Empty, new[] { asm }, Env()), Is.Null,
            "An empty surface emits no shim at all (NetShimGenerator's inertness rule), so there "
            + "is nothing to cache and no publish a hit could skip. Fix NetShimCache.KeyFor.");
    }

    // ==================================================================
    // Component 3 — the shim template version.
    // ==================================================================

    [Test]
    public void TemplateIdentityCarriesEveryTemplateInput()
    {
        var identity = NetShimCache.TemplateIdentity();

        Assert.Multiple(() =>
        {
            Assert.That(identity, Does.Contain(BlnetContract.AbiVersion.ToString()),
                "BlnetContract.AbiVersion is missing from the shim template identity — an ABI bump "
                + "would then hit the cache and ship a shim the native side cannot bind. Fix "
                + "NetShimCache.TemplateIdentity.");
            Assert.That(identity, Does.Contain(Sha256Hex(BlnetShimSources.HandleTable)),
                "BlnetShimSources.HandleTable is missing from the shim template identity. Fix "
                + "NetShimCache.TemplateIdentity.");
            Assert.That(identity, Does.Contain(Sha256Hex(BlnetShimSources.BlnetStatusCs)),
                "BlnetShimSources.BlnetStatusCs is missing from the shim template identity. Fix "
                + "NetShimCache.TemplateIdentity.");
            Assert.That(identity, Does.Contain(Sha256Hex(BlnetShimSources.ShimAbiCs)),
                "BlnetShimSources.ShimAbiCs is missing from the shim template identity. Fix "
                + "NetShimCache.TemplateIdentity.");
            Assert.That(identity,
                Does.Contain(typeof(NetShimCache).Assembly.ManifestModule.ModuleVersionId.ToString("N")),
                "The compiler assembly's own MVID is missing from the shim template identity — "
                + "without it, editing NetShimGenerator's emission logic leaves every cached shim "
                + "looking current. Fix NetShimCache.TemplateIdentity.");
        });
    }

    [Test]
    public void TheTemplateIdentityParticipatesInTheKey()
    {
        var asm = Path.Combine(_dir, "Probe.dll");
        EmitProbeAssembly(asm, "public class C { }");

        var withReal = NetShimCache.KeyFor(
            Surface(), new[] { asm }, Env(), NetShimCache.TemplateIdentity());
        var withOther = NetShimCache.KeyFor(
            Surface(), new[] { asm }, Env(), NetShimCache.TemplateIdentity() + "-next");

        Assert.Multiple(() =>
        {
            Assert.That(withReal, Is.EqualTo(NetShimCache.KeyFor(Surface(), new[] { asm }, Env())),
                "The three-argument KeyFor must be the four-argument one with TemplateIdentity() — "
                + "if they diverge, the seam stops testing production. Fix NetShimCache.KeyFor.");
            Assert.That(withOther, Is.Not.EqualTo(withReal),
                "A changed shim template must change the key. Fix NetShimCache.KeyFor: the "
                + "template component is not reaching the hash.");
        });
    }

    // ==================================================================
    // Components 4 and 5 — configuration, TFM, RID, toolchain, SDK.
    // ==================================================================

    [Test]
    public void KeyChangesWithEveryEnvironmentField()
    {
        var asm = Path.Combine(_dir, "Probe.dll");
        EmitProbeAssembly(asm, "public class C { }");

        string Key(NetShimCacheEnvironment env) => NetShimCache.KeyFor(Surface(), new[] { asm }, env);
        var baseline = Key(Env());

        Assert.Multiple(() =>
        {
            Assert.That(Key(Env(configuration: "Debug")), Is.Not.EqualTo(baseline),
                "Configuration must be in the KEY, not only in the manifest path — the path "
                + "segment is sanitized and sanitization is lossy. Fix NetShimCache.KeyFor.");
            Assert.That(Key(Env(tfm: "net9.0")), Is.Not.EqualTo(baseline),
                "TFM is in spec §10.2's key row. Fix NetShimCache.KeyFor.");
            Assert.That(Key(Env(rid: "linux-x64")), Is.Not.EqualTo(baseline),
                "RID is in spec §10.2's key row — a win-x64 shim is not a linux-x64 shim. Fix "
                + "NetShimCache.KeyFor.");
            Assert.That(Key(Env(toolchain: "g++ 14.2.0")), Is.Not.EqualTo(baseline),
                "Toolchain identity is in spec §10.2's key row. Fix NetShimCache.KeyFor.");
            Assert.That(Key(Env(sdk: "9.0.100")), Is.Not.EqualTo(baseline),
                "The .NET SDK version selects the ILCompiler that produces the native shim — "
                + "spec §10.2's last key row. Missing it means an SDK upgrade silently reuses the "
                + "shim the OLD ILCompiler built. Fix NetShimCache.KeyFor.");
        });
    }

    [Test]
    public void AnyBlankEnvironmentFieldMakesTheKeyNull()
    {
        var asm = Path.Combine(_dir, "Probe.dll");
        EmitProbeAssembly(asm, "public class C { }");

        string Key(NetShimCacheEnvironment env) => NetShimCache.KeyFor(Surface(), new[] { asm }, env);

        Assert.Multiple(() =>
        {
            Assert.That(Key(Env(configuration: "")), Is.Null);
            Assert.That(Key(Env(tfm: null!)), Is.Null);
            Assert.That(Key(Env(rid: "  ")), Is.Null);
            Assert.That(Key(Env(toolchain: "")), Is.Null);
            Assert.That(Key(Env(sdk: null!)), Is.Null,
                "An UNKNOWN SDK version must disable the cache, never be folded in as an empty "
                + "string — two different SDKs would then key identically and the second would "
                + "reuse the first's shim. Fix NetShimCache.KeyFor.");
        });
    }

    [Test]
    public void KeyIsStableAcrossRepeatedCalls()
    {
        var asm = Path.Combine(_dir, "Probe.dll");
        EmitProbeAssembly(asm, "public class C { }");

        var first = NetShimCache.KeyFor(Surface(), new[] { asm }, Env());
        var second = NetShimCache.KeyFor(Surface(), new[] { asm }, Env());

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null.And.Length.EqualTo(64), "SHA-256 as lowercase hex");
            Assert.That(second, Is.EqualTo(first),
                "The key must be a pure function of its inputs. If this fails, something in the "
                + "chain is using string.GetHashCode() (randomized per process in .NET Core, the "
                + "trap Task 9 hit) or otherwise carrying ambient state. Fix NetShimCache.KeyFor "
                + "or NetNameMangler.");
        });
    }

    // ==================================================================
    // The manifest.
    // ==================================================================

    private string PublishFakeDll(string content = "native shim bytes")
    {
        var dir = NetShimCache.PublishDirectory(_dir, Config);
        Directory.CreateDirectory(dir);
        var dll = Path.Combine(dir, "App.Blnet.dll");
        File.WriteAllText(dll, content);
        return dll;
    }

    [Test]
    public void CommitThenTryGetHitReturnsThePublishedDll()
    {
        var dll = PublishFakeDll();
        var key = new string('a', 64);

        Assert.That(NetShimCache.Commit(_dir, Config, key, dll), Is.True);
        var hit = NetShimCache.TryGetHit(_dir, Config, key);

        Assert.That(hit, Is.Not.Null, "a committed publish must be reusable — otherwise the cache "
            + "never hits and the ordinary edit-run loop pays 11 s forever");
        Assert.That(hit!.DllPath, Is.EqualTo(Path.GetFullPath(dll)));
    }

    [Test]
    public void ANullKeyIsAlwaysAMiss()
    {
        var dll = PublishFakeDll();
        NetShimCache.Commit(_dir, Config, new string('a', 64), dll);

        Assert.That(NetShimCache.TryGetHit(_dir, Config, null), Is.Null,
            "A null key means the inputs could not be identified, which must publish "
            + "unconditionally. Fix NetShimCache.TryGetHit.");
    }

    [Test]
    public void ADifferentKeyIsAMiss()
    {
        var dll = PublishFakeDll();
        NetShimCache.Commit(_dir, Config, new string('a', 64), dll);

        Assert.That(NetShimCache.TryGetHit(_dir, Config, new string('b', 64)), Is.Null,
            "Fix NetShimCache.TryGetHit: it is not comparing the recorded key.");
    }

    [Test]
    public void AMissingManifestIsAMiss()
    {
        Assert.That(NetShimCache.TryGetHit(_dir, Config, new string('a', 64)), Is.Null,
            "Spec §10.2: a missing manifest is a miss, never a silent stale hit. Fix "
            + "NetShimCache.TryGetHit.");
    }

    [TestCase("", TestName = "AnUnparsableManifestIsAMiss(empty)")]
    [TestCase("garbage", TestName = "AnUnparsableManifestIsAMiss(garbage)")]
    [TestCase("blnet-shim-cache/0\nKEY\nHASH\nC:\\x.dll\n",
        TestName = "AnUnparsableManifestIsAMiss(wrong format marker)")]
    [TestCase("blnet-shim-cache/1\nnot-hex\nHASH\nC:\\x.dll\n",
        TestName = "AnUnparsableManifestIsAMiss(key is not a hash)")]
    [TestCase("blnet-shim-cache/1\n", TestName = "AnUnparsableManifestIsAMiss(truncated)")]
    // The nastiest shape: every line that IS present is well-formed, and only the DLL path is
    // absent. A parser without a line-count guard indexes past the end here and throws out of
    // TryGetHit, which is not a miss — it is an unhandled exception on the build path.
    [TestCase("blnet-shim-cache/1\nKEY\nHASH",
        TestName = "AnUnparsableManifestIsAMiss(no trailing newline, path line absent)")]
    public void AnUnparsableManifestIsAMiss(string manifestText)
    {
        var dll = PublishFakeDll();
        var key = new string('a', 64);
        NetShimCache.Commit(_dir, Config, key, dll);

        // Splice the real hash in so only the defect under test differs.
        var real = File.ReadAllText(NetShimCache.ManifestPath(_dir, Config)).Replace("\r\n", "\n");
        var realHash = real.Split('\n')[2];
        File.WriteAllText(NetShimCache.ManifestPath(_dir, Config),
            manifestText.Replace("KEY", key).Replace("HASH", realHash));

        Assert.That(NetShimCache.TryGetHit(_dir, Config, key), Is.Null,
            "Spec §10.2: an unparsable manifest is a miss, never a silent stale hit. Fix "
            + "NetShimCache's manifest parser — there must be no lenient branch.");
    }

    [Test]
    public void ADeletedDllIsAMiss()
    {
        var dll = PublishFakeDll();
        var key = new string('a', 64);
        NetShimCache.Commit(_dir, Config, key, dll);
        File.Delete(dll);

        Assert.That(NetShimCache.TryGetHit(_dir, Config, key), Is.Null,
            "The manifest vouches for a DLL that no longer exists. Fix NetShimCache.TryGetHit.");
    }

    [Test]
    public void ADllReplacedOutOfBandIsAMiss()
    {
        var dll = PublishFakeDll("original shim");
        var key = new string('a', 64);
        NetShimCache.Commit(_dir, Config, key, dll);

        File.WriteAllText(dll, "something else entirely");

        Assert.That(NetShimCache.TryGetHit(_dir, Config, key), Is.Null,
            "THE crash-safety property: the manifest records the DLL's own SHA-256, so a publish "
            + "that overwrote the DLL and then died before rewriting the manifest cannot leave the "
            + "old key vouching for the new binary. Fix NetShimCache.TryGetHit — it must re-hash "
            + "the file, not just check that it exists.");
    }

    [Test]
    public void CommitAfterAFailedPublishWritesNothing()
    {
        var missing = Path.Combine(NetShimCache.PublishDirectory(_dir, Config), "App.Blnet.dll");
        var key = new string('a', 64);

        Assert.Multiple(() =>
        {
            Assert.That(NetShimCache.Commit(_dir, Config, key, missing), Is.False,
                "write-after-success: a publish that produced no DLL must not be recorded");
            Assert.That(File.Exists(NetShimCache.ManifestPath(_dir, Config)), Is.False,
                "a failed Commit must leave no manifest behind at all. Fix NetShimCache.Commit.");
            Assert.That(NetShimCache.TryGetHit(_dir, Config, key), Is.Null);
        });
    }

    [Test]
    public void CommitWithNoKeyWritesNothing()
    {
        var dll = PublishFakeDll();

        Assert.Multiple(() =>
        {
            Assert.That(NetShimCache.Commit(_dir, Config, null, dll), Is.False);
            Assert.That(NetShimCache.Commit(_dir, Config, "not-a-hash", dll), Is.False,
                "a key the manifest reader would reject must be refused at write time, not "
                + "written into a manifest that can never hit. Fix NetShimCache.Commit.");
            Assert.That(File.Exists(NetShimCache.ManifestPath(_dir, Config)), Is.False,
                "an unkeyable publish must record nothing. Fix NetShimCache.Commit.");
        });
    }

    [Test]
    public void CommitLeavesNoTemporaryFileBehind()
    {
        var dll = PublishFakeDll();
        NetShimCache.Commit(_dir, Config, new string('a', 64), dll);

        Assert.That(File.Exists(NetShimCache.ManifestPath(_dir, Config) + ".tmp"), Is.False,
            "the temp-then-rename write must rename, not copy. Fix NetShimCache.Commit.");
    }

    [Test]
    public void CommitOverwritesAPreviousManifest()
    {
        var dll = PublishFakeDll("first");
        var first = new string('a', 64);
        NetShimCache.Commit(_dir, Config, first, dll);

        File.WriteAllText(dll, "second");
        var second = new string('b', 64);
        Assert.That(NetShimCache.Commit(_dir, Config, second, dll), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(NetShimCache.TryGetHit(_dir, Config, second), Is.Not.Null);
            Assert.That(NetShimCache.TryGetHit(_dir, Config, first), Is.Null,
                "the superseded key must no longer hit. Fix NetShimCache.Commit.");
        });
    }

    [Test]
    public void InvalidateDropsTheManifest()
    {
        var dll = PublishFakeDll();
        var key = new string('a', 64);
        NetShimCache.Commit(_dir, Config, key, dll);

        Assert.Multiple(() =>
        {
            Assert.That(NetShimCache.Invalidate(_dir, Config), Is.True);
            Assert.That(NetShimCache.TryGetHit(_dir, Config, key), Is.Null);
            Assert.That(NetShimCache.Invalidate(_dir, Config), Is.True,
                "invalidating twice is not an error");
        });
    }

    [Test]
    public void ManifestsDoNotCollideBetweenConfigurations()
    {
        var releaseDll = PublishFakeDll("release bytes");
        var key = new string('a', 64);
        NetShimCache.Commit(_dir, Config, key, releaseDll);

        Assert.Multiple(() =>
        {
            Assert.That(NetShimCache.TryGetHit(_dir, Config, key), Is.Not.Null);
            Assert.That(NetShimCache.TryGetHit(_dir, "Debug", key), Is.Null,
                "Debug and Release must not share a manifest. Fix NetShimCache.CacheDirectory.");
        });
    }

    // ==================================================================
    // Where the cache lives — the `clean` contract.
    // ==================================================================

    [Test]
    public void TheCacheLivesUnderObjAndOutsideObjGen()
    {
        var root = NetShimCache.CacheRoot(_dir);

        Assert.Multiple(() =>
        {
            Assert.That(root, Is.EqualTo(Path.Combine(_dir, "obj", "blnet")),
                "The cache must live under obj/ so `clean` can drop it (spec §10.2). If this "
                + "moves, update BuildService.CleanAsync to match.");
            Assert.That(NetShimCache.ManifestPath(_dir, Config), Does.StartWith(root),
                "every manifest must be under the one directory `clean` deletes");
            Assert.That(NetShimCache.PublishDirectory(_dir, Config), Does.StartWith(root));
            Assert.That(root, Does.Not.StartWith(Path.Combine(_dir, "obj", "gen")),
                "The cache must NOT be under obj/gen: CppProjectBuilder.CleanGeneratedDir sweeps "
                + "that directory on every successful build, and a cache a routine build wipes is "
                + "not a cache. Fix NetShimCache.CacheRoot.");
        });
    }

    [Test]
    public void AnExoticConfigurationNameStillProducesAUsablePath()
    {
        var path = NetShimCache.ManifestPath(_dir, "Debug|Any CPU");

        Assert.Multiple(() =>
        {
            Assert.That(Path.GetInvalidFileNameChars()
                    .Any(c => Path.GetFileName(Path.GetDirectoryName(path)!).Contains(c)),
                Is.False,
                "the configuration segment must be sanitized. Fix NetShimCache.CacheDirectory.");
            Assert.DoesNotThrow(() => Directory.CreateDirectory(Path.GetDirectoryName(path)!));
        });
    }

    // ==================================================================
    // FrameworkPaths must not become host-dependent — the measured claim
    // behind hashing the framework closure at all.
    // ==================================================================

    /// <summary>
    /// The hazard this pins is the complement of
    /// <c>NetReferenceResolverTests.FrameworkPaths_ContainOnlyAssembliesFromTheSharedFrameworkDirectory</c>.
    /// That test catches a HOST dependency leaking IN; this one catches a framework assembly
    /// silently dropping OUT.
    ///
    /// <para>If a host ships its own copy of a framework assembly beside itself, hostpolicy puts
    /// THAT path in <c>TRUSTED_PLATFORM_ASSEMBLIES</c> instead of the shared framework's, and
    /// <c>NetReferenceResolver</c>'s shared-framework directory filter then drops the assembly
    /// from <c>FrameworkPaths</c> entirely. The set would differ between
    /// <c>BasicLang.exe</c>, <c>VisualGameStudio.exe</c> and the test host — so alternating a CLI
    /// build and an IDE build on one project would key differently every time and the cache would
    /// never hit.</para>
    /// </summary>
    [Test]
    public void NoHostLocalAssemblyShadowsAFrameworkAssembly()
    {
        var frameworkDir = NetReferenceResolver.FrameworkDirectoryForTests;
        Assert.That(frameworkDir, Is.Not.Null.And.Not.Empty,
            "guard: the shared-framework directory must be discoverable");

        var frameworkNames = NetReferenceResolver.FrameworkAssemblies
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.That(frameworkNames, Is.Not.Empty, "guard: the framework set must not be empty");

        var shadows = Directory
            .EnumerateFiles(AppContext.BaseDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => frameworkNames.Contains(n!))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.That(shadows, Is.Empty,
            "A host-local copy of a shared-framework assembly makes NetReferenceClosure."
            + "FrameworkPaths host-dependent (the shared-framework directory filter drops the "
            + "shadowed entry), which makes NetShimCache's key host-dependent: the same project "
            + "built from the IDE and from BasicLang.exe would never share a cache entry. Fix the "
            + "PROJECT that is shipping the assembly locally (remove the package reference or the "
            + "explicit copy) — do not relax NetReferenceResolver's filter, which exists to keep "
            + "the IDE's ~40 non-framework DLLs out of the closure.");
    }

    // ==================================================================
    // The SDK probe. Spawns a process -> Integration.
    // ==================================================================

    [Test]
    [Category("Integration")]
    public void ProbeSdkIdentityAnswersAVersionOrNull()
    {
        var version = NetShimCache.ProbeSdkIdentity(_dir);

        // The SDK is on PATH on any machine that can run this suite, but the contract is
        // "a version or null" — never an empty string, which would key as "known".
        if (version == null)
            Assert.Pass("no .NET SDK reachable from PATH — null is the correct (miss) answer");

        Assert.Multiple(() =>
        {
            Assert.That(version, Is.Not.Empty,
                "ProbeSdkIdentity must answer null for an unknown SDK, never an empty string — an "
                + "empty string folds into the key as though the SDK were known. Fix "
                + "NetShimCache.ProbeSdkIdentity.");
            Assert.That(version, Does.Match(@"^\d+\.\d+\.\d"), "a dotnet --version string");
        });
    }

    [Test]
    [Category("Integration")]
    public void ProbeSdkIdentityIsMemoizedPerWorkingDirectory()
    {
        Assert.That(NetShimCache.ProbeSdkIdentity(_dir),
            Is.EqualTo(NetShimCache.ProbeSdkIdentity(_dir)),
            "the probe must be stable within a process. Fix NetShimCache.ProbeSdkIdentity.");
    }

    [Test]
    [Category("Integration")]
    public void ProbeSdkIdentityAnswersNullForANonExistentDirectory()
    {
        Assert.That(NetShimCache.ProbeSdkIdentity(Path.Combine(_dir, "nope", "nope")), Is.Null,
            "an undeterminable SDK must be null (a permanent miss), never a guess. Fix "
            + "NetShimCache.ProbeSdkIdentity.");
    }
}

/// <summary>
/// The IDE half of spec §10.2's last sentence: "<c>Clean</c> today deletes <c>bin/&lt;config&gt;</c>
/// but not <c>obj/</c>; it must also drop the shim cache."
///
/// <para>The pair of assertions is the whole point. A clean that misses the cache leaves a
/// manifest that can still vouch for a DLL the user just asked to be rebuilt; a clean that
/// deletes <c>obj/</c> wholesale takes <c>obj/gen</c> with it — the generated C++ headers clangd
/// is serving right now, and the C# backend's restore state — which <c>clean</c> has never
/// touched.</para>
/// </summary>
[TestFixture]
public class BuildServiceShimCacheCleanTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "blnet-clean-" + Guid.NewGuid().ToString("N"));
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

    [Test]
    public async Task CleanDropsTheShimCacheAndNothingElseUnderObj()
    {
        var project = new BasicLangProject { FilePath = Path.Combine(_dir, "App.blproj") };
        project.Configurations["Release"] =
            new BuildConfiguration { Name = "Release", OutputPath = Path.Combine("bin", "Release") };

        var binDir = Path.Combine(_dir, "bin", "Release");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(binDir, "App.exe"), "exe");

        var objGen = Path.Combine(_dir, "obj", "gen");
        Directory.CreateDirectory(objGen);
        File.WriteAllText(Path.Combine(objGen, "Logic.g.h"), "// clangd is serving this");
        var assets = Path.Combine(_dir, "obj", "project.assets.json");
        File.WriteAllText(assets, "{}");

        var dll = Path.Combine(NetShimCache.PublishDirectory(_dir, "Release"), "App.Blnet.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        File.WriteAllText(dll, "native shim bytes");
        var key = new string('a', 64);
        Assert.That(NetShimCache.Commit(_dir, "Release", key, dll), Is.True, "guard: manifest written");

        var service = new BuildService(new Mock<IOutputService>().Object);
        await service.CleanAsync(project);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(NetShimCache.CacheRoot(_dir)), Is.False,
                "Spec §10.2: clean must drop the shim cache, or the next build skips a publish "
                + "the user explicitly asked to be redone. Fix BuildService.CleanAsync.");
            Assert.That(NetShimCache.TryGetHit(_dir, "Release", key), Is.Null);
            Assert.That(Directory.Exists(binDir), Is.False, "clean's pre-existing behavior");
            Assert.That(File.Exists(Path.Combine(objGen, "Logic.g.h")), Is.True,
                "clean must NOT widen to obj/gen — those headers are what clangd serves, and "
                + "deleting them would take every C++ completion with them. Fix "
                + "BuildService.CleanAsync: it is deleting more than the shim cache.");
            Assert.That(File.Exists(assets), Is.True,
                "clean must NOT widen to obj/ — the C# backend's restore state lives there and "
                + "clean has never touched it. Fix BuildService.CleanAsync.");
        });
    }
}
