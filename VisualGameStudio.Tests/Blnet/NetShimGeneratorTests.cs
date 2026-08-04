using System.Text.RegularExpressions;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.Net;
using BasicLang.Net;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// <see cref="NetShimGenerator"/> — the managed half of the P2a-1 boundary: the shim
/// <c>.csproj</c> and <c>Exports.g.cs</c>. Pure text assertions live here; the one test that
/// actually proves the emitted C# is real C# — a Native-AOT publish — is
/// <see cref="NetShimGeneratorPublishTests"/>, kept in its own fixture so the string pins
/// never pay for a `dotnet publish`.
///
/// <para><b>On the §12.4 slots-≡-exports test.</b> Both sides ultimately call
/// <see cref="NetNameMangler"/> on the same descriptors, so
/// <see cref="ProxyTableSlotsMatchTheSurfaceDerivedExports"/> is a WIRING TRIPWIRE, not a drift
/// oracle: it catches "someone gave the shim generator its own naming scheme" or "the two
/// de-duplicate on different keys", and nothing subtler. The test with real independent
/// derivation is <see cref="ExportSignaturesMatchTheProxyTableSlotSignatures"/>, which compares
/// the C function-pointer signature <c>NetProxyEmitter</c> emits against the C# signature this
/// generator emits, through a C-to-C# type table that belongs to neither producer. A mismatch
/// there is not a cosmetic drift — it is the native side calling a managed export with the
/// wrong argument widths.</para>
/// </summary>
[TestFixture]
public class NetShimGeneratorTests
{
    private const string SafeProject = "ProbeApp";

    // ---- Fixture surfaces ---------------------------------------------------------------
    // Built per call, never cached: NetSurface's == is reference equality (its own remarks).

    private static NetParameterDescriptor P(string type, NetRefKind refKind = NetRefKind.None) =>
        new(refKind, type);

    private static NetMemberDescriptor Member(
        string name, string declaringType, NetMemberCategory kind, bool isStatic,
        string type, params NetParameterDescriptor[] parameters) =>
        new(name, declaringType, kind, isStatic, 0, type, parameters);

    /// <summary>The §12.4 pair's surface — deliberately the same one NetProxyEmitterTests uses.</summary>
    private static NetSurface OneMemberSurface() => NetProxyEmitterTests.OneMemberSurface();

    /// <summary>A handle-returning member taking a handle parameter — §8.2's two null rules.</summary>
    private static NetSurface HandleShapeSurface() => new(
        new[]
        {
            Member("Wrap", "MyLib.Sink", NetMemberCategory.Method,
                   false, "MyLib.Payload", P("MyLib.Payload")),
        },
        Array.Empty<string>());

    /// <summary>A value-type receiver — §8.5's <c>Unsafe.Unbox&lt;T&gt;</c> rule.</summary>
    private static NetSurface ValueTypeReceiverSurface() => new(
        new[]
        {
            Member("AddDays", "System.DateTime", NetMemberCategory.Method,
                   false, "System.DateTime", P("System.Double")),
        },
        Array.Empty<string>());

    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "blnet-shim-" + Guid.NewGuid().ToString("N"));

    private static string N(string s) => s.Replace("\r\n", "\n");

    // ---- §8.1 project shape --------------------------------------------------------------

    [Test]
    public void ProjectCarriesEverySpecifiedProperty()
    {
        var csproj = NetShimGenerator.EmitProject("ProbeApp.Blnet");

        Assert.Multiple(() =>
        {
            foreach (var property in new[]
            {
                "<TargetFramework>net8.0</TargetFramework>",
                "<PublishAot>true</PublishAot>",
                "<AllowUnsafeBlocks>true</AllowUnsafeBlocks>",
                "<InvariantGlobalization>true</InvariantGlobalization>",
                "<IsAotCompatible>true</IsAotCompatible>",
                "<TrimmerSingleWarn>false</TrimmerSingleWarn>",
            })
                Assert.That(csproj, Does.Contain(property),
                    $"The generated shim project is missing {property} (spec §8.1). Fix "
                    + "NetShimGenerator.EmitProject — every property in that list is load-bearing: "
                    + "net8.0 is the NU1201 floor, AllowUnsafeBlocks is required by the pointer "
                    + "signatures every export has, IsAotCompatible turns the four AOT analyzers "
                    + "on, and TrimmerSingleWarn=false is what stops ILC collapsing per-member "
                    + "warnings into one assembly-level line that Task 16's mapper cannot attribute.");
        });
    }

    /// <summary>
    /// <c>ImplicitUsings</c> is not decoration: <c>BlnetShimSources.HandleTable</c> — which the
    /// generated shim compiles verbatim — names <c>List&lt;T&gt;</c>, <c>Stack&lt;T&gt;</c> and
    /// LINQ's <c>Count(predicate)</c> with no <c>using</c> of its own.
    /// </summary>
    [Test]
    public void ProjectEnablesImplicitUsings_BecauseTheHandleTableDependsOnThem()
    {
        var csproj = NetShimGenerator.EmitProject("ProbeApp.Blnet");

        Assert.That(csproj, Does.Contain("<ImplicitUsings>enable</ImplicitUsings>"),
            "Task 11's binding decision (BlnetShimSources' remarks): the emitted csproj MUST set "
            + "ImplicitUsings=enable or HandleTable.g.cs does not compile. Fix "
            + "NetShimGenerator.EmitProject, not HandleTable.");
    }

    /// <summary>
    /// Three producers have to agree on ONE string or the program dies at startup with
    /// "blnet: failed to load '…'": the csproj basename (which is what
    /// <c>dotnet publish -p:NativeLib=Shared</c> names its output after, and what
    /// <c>NetShimPublisher.ExpectedDllPath</c> derives), and the module name literal baked into
    /// <c>blnet_startup.g.cpp</c>. Each is computed by a different class here.
    /// </summary>
    [Test]
    public void ShimModuleName_AgreesAcrossGeneratorPublisherAndStartupTu()
    {
        var files = NetShimGenerator.Emit(OneMemberSurface(), SafeProject);
        var csprojName = files.Keys.Single(k => k.EndsWith(".csproj", StringComparison.Ordinal));
        var startupTu = NetProxyEmitter.Emit(
            OneMemberSurface(),
            NetProxyEmitter.ShimModuleFileName(NetShimGenerator.ShimAssemblyName(SafeProject)))
            [NetProxyEmitter.StartupFileName];

        var publishedDll = Path.GetFileName(
            NetShimPublisher.ExpectedDllPath(Path.Combine("C:\\out", csprojName), "C:\\out"));

        Assert.Multiple(() =>
        {
            Assert.That(csprojName, Is.EqualTo("ProbeApp.Blnet.csproj"),
                "The shim project must be named from NetShimGenerator.ShimAssemblyName "
                + "(<SafeProject>.Blnet). Fix NetShimGenerator — do not retype the rule.");
            Assert.That(startupTu, Does.Contain($"kBlnetShimModule = \"{publishedDll}\""),
                "blnet_startup.g.cpp loads a module name that the published shim will not have. "
                + "The three sides are NetShimGenerator's csproj name, "
                + "NetShimPublisher.ExpectedDllPath and NetProxyEmitter.ShimModuleFileName — all "
                + "three must be fed NetShimGenerator.ShimAssemblyName's output.");
        });
    }

    // ---- §8.2 export pattern -------------------------------------------------------------

    [Test]
    public void ExportsIncludeP0sSevenCoreNames()
    {
        var cs = NetShimGenerator.EmitExports(OneMemberSurface());

        foreach (var name in BlnetContract.CoreExportNames)
            Assert.That(cs, Does.Contain($"EntryPoint = \"{name}\""),
                "A generated shim must export P0's seven core names too, not only surface-derived "
                + "wrappers — blnet_bind_core (Task 12) binds them at startup. blnet_abi_version must "
                + "return BlnetContract.AbiVersion and blnet_initialize must return "
                + "BLNET_E_VERSION_MISMATCH when the caller's ABI differs.");
    }

    [Test]
    public void CoreExportsCarryTheirContractedBehavior()
    {
        var cs = NetShimGenerator.EmitExports(OneMemberSurface());

        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Contain("=> ShimAbi.AbiVersion;"),
                "blnet_abi_version must report ShimAbi.AbiVersion, which BlnetShimSources splices "
                + "from BlnetContract.AbiVersion — a hard-coded literal here would let the shim "
                + "pass a handshake it should fail.");
            Assert.That(cs, Does.Contain("BlnetStatus.BLNET_E_VERSION_MISMATCH"),
                "blnet_initialize must return BLNET_E_VERSION_MISMATCH when the caller's ABI "
                + "differs (spec C7). Fix NetShimGenerator's core export block.");
        });
    }

    [Test]
    public void ProxyTableSlotsMatchTheSurfaceDerivedExports()
    {
        var surface = OneMemberSurface();
        var slots = NetProxyEmitter.EmitBindings(surface).SlotNames;
        var exports = NetShimGenerator.SurfaceDerivedExportNames(surface);

        Assert.That(slots, Is.EquivalentTo(exports),
            "Spec §12.4. Scoped to SURFACE-DERIVED exports deliberately — the shim also exports P0's "
            + "seven core names, which are not BlnetProxyTable slots, so an unscoped equality is "
            + "false by construction.\n"
            + "NOTE (Task 10): §8.6's array copy helpers are NOT an exemption here — this message "
            + "used to list them as one. They are slots AND exports, both derived from a single "
            + "NetArrayCopy.RequiredForms(surface), so they are INCLUDED on both sides and §12.4 "
            + "holds for them by construction. If this assertion fails over an array helper, the "
            + "bug is real drift between the two emitters, not a missing exemption.");
    }

    /// <summary>
    /// The independently-derived half of §12.4, and the one that can catch a real defect: for
    /// every slot, the C signature <c>NetProxyEmitter</c> declared and the C# signature this
    /// generator emitted must describe the same call. The C-to-C# table lives HERE, in neither
    /// producer, so agreeing is not automatic — the two wire tables really are separate code.
    /// </summary>
    [Test]
    public void ExportSignaturesMatchTheProxyTableSlotSignatures()
    {
        var surface = NetProxyEmitterTests.WireShapeSurface();
        var slots = ParseSlotSignatures(N(NetProxyEmitter.EmitBindings(surface).Text));
        var exports = ParseExportSignatures(N(NetShimGenerator.EmitExports(surface)));

        Assert.That(exports.Keys, Is.EquivalentTo(slots.Keys),
            "The shim exported a different set of names than the proxy table declares.");

        Assert.Multiple(() =>
        {
            foreach (var (name, cSignature) in slots)
            {
                var expected = cSignature.Select(a => (CsTypeFor(a.Type), a.Name)).ToArray();
                Assert.That(exports[name], Is.EqualTo(expected),
                    $"Slot '{name}' has C signature ({string.Join(", ", cSignature.Select(a => a.Type + " " + a.Name))}) "
                    + "but the generated C# export does not match it. Native code calls this "
                    + "through a function pointer with the C shape, so a width or arity mismatch "
                    + "is stack corruption, not a warning. Fix whichever side is wrong: "
                    + "NetProxyEmitter.WireOf (the C column) or NetShimGenerator.WireOf (the C# "
                    + "column) — they are two separate tables of the same spec §8.3 rows.");
            }
        });
    }

    [Test]
    public void HandleArgumentsAreGuardedAgainstZeroBeforeTheTableIsConsulted()
    {
        var cs = NetShimGenerator.EmitExports(HandleShapeSurface());

        Assert.Multiple(() =>
        {
            foreach (var handle in new[] { "self", "a0" })
            {
                var guard = cs.IndexOf($"if ({handle} != 0)", StringComparison.Ordinal);
                var lookup = cs.IndexOf($"Table.TryGet({handle}", StringComparison.Ordinal);

                Assert.That(guard, Is.GreaterThan(-1),
                    $"Handle argument '{handle}' reaches HandleTable unguarded. §8.2: handle 0 means "
                    + "Nothing and HandleTable.Validate rejects index 0, so an unconditional TryGet "
                    + "turns every null argument into BLNET_E_STALE_HANDLE. Fix NetShimGenerator — "
                    + "HandleTable must NOT be relaxed (P0's ZeroHandle_IsAlwaysStale pins it).");
                Assert.That(lookup, Is.GreaterThan(guard),
                    $"Handle argument '{handle}' is looked up in HandleTable before its zero guard.");
            }
        });
    }

    [Test]
    public void NullReturnEncodesAsHandleZeroWithoutMintingOne()
    {
        var cs = NetShimGenerator.EmitExports(HandleShapeSurface());

        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Contain("o is null ? 0UL : Table.Create(o)"),
                "§8.2: a null return must encode as handle 0. Table.Create(null) would mint a live "
                + "non-zero handle for Nothing, which the native NetRef would then release. Fix "
                + "NetShimGenerator's handle-encode helper.");
            Assert.That(cs, Does.Match(@"\*result = ToHandle\("),
                "The handle-returning wrapper must route its result through the null-encoding "
                + "helper rather than calling Table.Create directly.");
        });
    }

    // ---- §8.5 receivers ------------------------------------------------------------------

    [Test]
    public void ValueTypeReceiverUsesUnsafeUnbox()
    {
        var cs = NetShimGenerator.EmitExports(
            ValueTypeReceiverSurface(), valueTypeReceiverNames: new[] { "System.DateTime" });

        Assert.That(cs, Does.Contain("Unsafe.Unbox<global::System.DateTime>(self_!)"),
            "§8.5: a boxed value-type receiver must be reached through Unsafe.Unbox<T>, not an "
            + "unboxing cast. ((T)o!).Prop = v is CS0445 (the shim would not compile at all) and "
            + "((T)o!).Mutate() silently mutates the temporary the unboxing produced, leaving the "
            + "box untouched. Fix NetShimGenerator's receiver emission.");
    }

    /// <summary>
    /// A MUTABLE framework struct with a settable property, reached through the production
    /// synthesis point. <c>GCHandle.Target</c> is chosen because it is a real
    /// <c>public object? Target { get; set; }</c> on a struct — the shape §8.5's CS0445 rule is
    /// about — rather than something invented for the test.
    /// </summary>
    private const string MutableStruct = "System.Runtime.InteropServices.GCHandle";

    private static NetSurface MutableStructSetterSurface() => new(
        new[]
        {
            NetAccessorSynthesis.SetterFor(new NetMemberDescriptor(
                "Target", MutableStruct, NetMemberCategory.Property, isStatic: false, arity: 0,
                "System.Object", Array.Empty<NetParameterDescriptor>())),
        },
        Array.Empty<string>());

    /// <summary>
    /// The shared shim-compile harness — the ~25 s AOT publish's compile step in milliseconds.
    /// Moved to <see cref="NetStubHarness.CompileShim"/> in P2a-2 Task 10, when §8.6 became the
    /// second consumer; kept as a one-line delegation so the call sites below read unchanged.
    /// </summary>
    private static IReadOnlyList<Diagnostic> CompileShim(string exports) =>
        NetStubHarness.CompileShim(exports);

    /// <summary>
    /// <b>The §8.5 mutation oracle, and the reason <c>Unsafe.Unbox</c> is a RULE rather than an
    /// optimization.</b> The generated shim for a property SET on a boxed value-type receiver
    /// must compile; swapping the receiver back to the obvious unboxing cast must make it fail
    /// with <b>CS0445</b>, "cannot modify the result of an unboxing conversion".
    ///
    /// <para>Without the mutation half this test proves almost nothing: a shim that compiles is
    /// what you get either way for a READ, and the whole hazard is that the cast form looks
    /// correct. The mutation is applied to the GENERATED TEXT, so it exercises the exact
    /// spelling <see cref="NetShimGenerator"/> emits rather than a paraphrase of it.</para>
    /// </summary>
    [Test]
    public void ValueTypeReceiverSetter_CompilesWithUnsafeUnbox_AndIsCs0445WithACast()
    {
        var exports = NetShimGenerator.EmitExports(
            MutableStructSetterSurface(), valueTypeReceiverNames: new[] { MutableStruct });

        var unboxReceiver = "global::System.Runtime.CompilerServices.Unsafe.Unbox<global::"
                            + MutableStruct + ">(self_!)";
        Assert.That(exports, Does.Contain(unboxReceiver),
            "the fixture is not exercising the §8.5 receiver rule at all:\n" + exports);

        var clean = CompileShim(exports);
        Assert.That(clean, Is.Empty,
            "the GENERATED shim must compile. These are the errors the ~25 s AOT publish would "
            + "have reported against a file under obj/gen/shim that the user never wrote:\n"
            + string.Join("\n", clean));

        // THE MUTATION: Unsafe.Unbox<T>(o) -> ((T)o), the spelling §8.5 forbids.
        var mutated = exports.Replace(
            unboxReceiver, "((global::" + MutableStruct + ")self_!)");
        Assert.That(mutated, Is.Not.EqualTo(exports), "the mutation did not apply");

        var errors = CompileShim(mutated);
        Assert.That(errors.Select(d => d.Id), Contains.Item("CS0445"),
            "swapping Unsafe.Unbox<T> for an unboxing CAST must break the compile with CS0445 "
            + "('cannot modify the result of an unboxing conversion'). If it now compiles, the "
            + "generator stopped emitting a property SET through the receiver — and §8.5's OTHER "
            + "failure, a mutating METHOD call through a cast, has no compile error at all: it "
            + "mutates the temporary and loops forever. Errors were:\n"
            + string.Join("\n", errors));
    }

    [Test]
    public void ReferenceTypeReceiverUsesAPlainCast()
    {
        var cs = NetShimGenerator.EmitExports(ValueTypeReceiverSurface());

        Assert.That(cs, Does.Contain("((global::System.DateTime)self_!)"),
            "A receiver the caller did NOT report as a value type must use a plain cast — "
            + "Unsafe.Unbox<T> does not compile for a reference type (it is constrained to "
            + "struct). NetShimGenerator must key on valueTypeReceiverNames, never on a name "
            + "pattern.");
    }

    // ---- Inertness (the P2a-1 claim, on the managed side) --------------------------------

    /// <summary>
    /// The managed mirror of <c>NetProxyEmitterTests.EmptySurfaceWritesNoFileAtAll</c>. Every
    /// project in the repo has an empty surface; a shim csproj appearing under <c>obj/gen</c>
    /// for one of them is P2a-1 changing a build it promised not to touch — and unlike a stray
    /// header, a stray csproj is something an IDE or a `dotnet build` sweep can pick up.
    /// </summary>
    [Test]
    public void EmptySurfaceWritesNoFileAtAll()
    {
        var dir = TempDir();
        try
        {
            var written = NetShimGenerator.WriteTo(dir, NetSurface.Empty, SafeProject);

            Assert.Multiple(() =>
            {
                Assert.That(written, Is.Empty,
                    "NetShimGenerator.WriteTo reported writing shim files for an EMPTY .NET "
                    + "surface. Emission must stay inside the surface.IsNonEmpty branch.");
                Assert.That(Directory.Exists(dir), Is.False,
                    "NetShimGenerator.WriteTo CREATED the shim directory for an empty surface. "
                    + "Directory.CreateDirectory belongs inside the non-empty branch.");
            });
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    /// <summary>
    /// <b>7b-I6: the shim csproj is an SDK project rooted in <c>obj/gen/shim</c>, so it globs
    /// <c>**/*.cs</c> — an orphan left by an earlier build is COMPILED INTO the shim.</b>
    /// Latent while the emitted file set was fixed; Tasks 9/10/11 make it surface-dependent
    /// (per-element array helpers, per-delegate dispatchers), so a removed member's orphaned
    /// wrapper would keep compiling against a member the surface no longer has.
    ///
    /// <para>The two negative assertions are the ones with teeth. <c>obj/</c> holds the
    /// publish's own working state and is where a recursive delete would do real damage; a
    /// <c>.log</c> stands for everything a user or tool may legitimately leave here. The
    /// extension filter plus a NON-recursive enumeration is what keeps both safe, and both
    /// would go red if either property were relaxed.</para>
    /// </summary>
    [Test]
    public void WriteToPrunesStaleSourcesButLeavesEverythingElse()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            var orphanSource = Path.Combine(dir, "Exports.Removed.g.cs");
            var orphanProject = Path.Combine(dir, "Stale.Blnet.csproj");
            var keepLog = Path.Combine(dir, "publish.log");
            var objDir = Path.Combine(dir, "obj");
            Directory.CreateDirectory(objDir);
            var keepObj = Path.Combine(objDir, "project.assets.json");

            File.WriteAllText(orphanSource, "// a wrapper for a member the surface no longer has");
            File.WriteAllText(orphanProject, "<Project/>");
            File.WriteAllText(keepLog, "not ours to delete");
            File.WriteAllText(keepObj, "{}");

            NetShimGenerator.WriteTo(dir, OneMemberSurface(), SafeProject);

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(orphanSource), Is.False,
                    "a stale *.g.cs survived. The SDK globs this directory, so it would be "
                    + "compiled into the shim — a wrapper for a member the surface no longer "
                    + "contains.");
                Assert.That(File.Exists(orphanProject), Is.False,
                    "a stale *.csproj survived. Two project files in one directory is an "
                    + "ambiguous publish, and the wrong one may name the wrong AssemblyName — "
                    + "which is the file blnet_startup.g.cpp loads by BARE NAME.");
                Assert.That(File.Exists(keepLog), Is.True,
                    "the prune deleted a non-source file. It must only ever remove files whose "
                    + "presence CHANGES what the shim compiles to; anything else here is not "
                    + "ours.");
                Assert.That(File.Exists(keepObj), Is.True,
                    "the prune reached into obj/ — the publish's own working state. The "
                    + "enumeration is deliberately NON-recursive for exactly this reason.");
                Assert.That(File.Exists(Path.Combine(dir, NetShimGenerator.ExportsFileName)),
                    Is.True, "…and it still wrote the real artifact set.");
            });
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Test]
    public void NonEmptySurfaceEmitsTheProjectPlusFiveSources()
    {
        var files = NetShimGenerator.Emit(OneMemberSurface(), SafeProject);

        Assert.That(files.Keys, Is.EquivalentTo(new[]
        {
            "ProbeApp.Blnet.csproj",
            NetShimGenerator.ExportsFileName,
            NetShimGenerator.HandleTableFileName,
            NetShimGenerator.StatusFileName,
            NetShimGenerator.ShimAbiFileName,
            NetShimGenerator.MarshalFileName,
        }), "The shim's file set changed. §8.1's inventory is: the csproj, the generated "
          + "Exports.g.cs, and the fixed scaffolding files that come from BlnetShimSources "
          + "(three hand-shim-pinned ones plus §6.4's BlnetMarshal.cs, P2a-2 Task 6).");
    }

    /// <summary>
    /// The scaffolding must be SPLICED from <c>BlnetShimSources</c>, not re-emitted here — that
    /// is the whole point of Task 11's three drift invariants, which tie the generated handle
    /// model to the hand shim the frozen P0 conformance suite validates.
    /// </summary>
    [Test]
    public void ScaffoldingComesFromBlnetShimSourcesVerbatim()
    {
        var files = NetShimGenerator.Emit(OneMemberSurface(), SafeProject);

        Assert.Multiple(() =>
        {
            Assert.That(N(files[NetShimGenerator.HandleTableFileName]),
                Is.EqualTo(N(BlnetShimSources.HandleTable)),
                "HandleTable.g.cs is not BlnetShimSources.HandleTable verbatim. The frozen P0 "
                + "conformance suite only says anything about generated shims while the two "
                + "handle models are the same one.");
            Assert.That(N(files[NetShimGenerator.StatusFileName]),
                Is.EqualTo(N(BlnetShimSources.BlnetStatusCs)));
            Assert.That(N(files[NetShimGenerator.ShimAbiFileName]),
                Is.EqualTo(N(BlnetShimSources.ShimAbiCs)));
            Assert.That(N(files[NetShimGenerator.MarshalFileName]),
                Is.EqualTo(N(BlnetShimSources.MarshalCs)),
                "BlnetMarshal.cs is not BlnetShimSources.MarshalCs verbatim — §6.4's managed "
                + "conversions must have exactly one source of truth (NetConversionPairTests "
                + "pins the vectors against that constant, not against a re-emission).");
        });
    }

    /// <summary>
    /// Task 11's namespace decision, restated as an assertion because breaking it is a compile
    /// error nothing else in this fixture would catch: <c>Exports.g.cs</c> lives in the same
    /// namespace as <c>HandleTable</c>, and <c>BlnetStatus</c> deliberately does not.
    /// </summary>
    [Test]
    public void ExportsDeclareTheShimNamespaceSoTheyCanSeeHandleTable()
    {
        var cs = NetShimGenerator.EmitExports(OneMemberSurface());

        Assert.That(N(cs), Does.Contain($"namespace {NetShimGenerator.ShimNamespace};"),
            "Exports.g.cs must declare BlnetShimSources' namespace or it cannot see HandleTable "
            + "(Task 11's binding decision). BlnetStatus is the one type that stays in the global "
            + "namespace — it is pinned byte-equal to BlnetContract.GenerateStatusEnumCs, which "
            + "emits no namespace line. Do not 'fix' that by wrapping it.");
    }

    // ---- Signature parsing helpers (test-owned; belong to neither producer) ---------------

    private static readonly Regex SlotLine = new(
        @"^\s*int32_t \(BLNET_CALL \*(?<name>\w+)\)\((?<sig>[^)]*)\);\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex ExportLine = new(
        @"^\s*public static int (?<name>bl_net_\w+)\((?<sig>[^)]*)\)\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static Dictionary<string, (string Type, string Name)[]> ParseSlotSignatures(string header) =>
        SlotLine.Matches(header).ToDictionary(
            m => m.Groups["name"].Value,
            m => SplitArguments(m.Groups["sig"].Value));

    private static Dictionary<string, (string Type, string Name)[]> ParseExportSignatures(string cs) =>
        ExportLine.Matches(cs).ToDictionary(
            m => m.Groups["name"].Value,
            m => SplitArguments(m.Groups["sig"].Value));

    private static (string Type, string Name)[] SplitArguments(string signature)
    {
        if (signature.Trim() is "" or "void") return Array.Empty<(string, string)>();
        return signature.Split(',')
            .Select(a => a.Trim())
            .Select(a =>
            {
                var split = a.LastIndexOf(' ');
                return (a[..split].Trim(), a[(split + 1)..].Trim());
            })
            .ToArray();
    }

    /// <summary>Spec §8.3's wire rows, spelled in C on one side and C# on the other.</summary>
    private static string CsTypeFor(string cType) => cType switch
    {
        "void" => "void",
        "int8_t" => "sbyte",
        "uint8_t" => "byte",
        "int16_t" => "short",
        "uint16_t" => "ushort",
        "int32_t" => "int",
        "uint32_t" => "uint",
        "int64_t" => "long",
        "uint64_t" => "ulong",
        "float" => "float",
        "double" => "double",
        "const char*" => "byte*",
        "char**" => "byte**",
        _ when cType.EndsWith("*", StringComparison.Ordinal) =>
            CsTypeFor(cType[..^1]) + "*",
        _ => "<no C# wire form for " + cType + ">",
    };
}

/// <summary>
/// THE load-bearing test of Task 14: a shim generated from a hand-fed surface is handed to the
/// product publisher and must come out the other side as a native DLL. Everything else in
/// <see cref="NetShimGeneratorTests"/> asserts that a string contains a substring; only this one
/// proves the emitted text is C# that Roslyn accepts and ILC can compile ahead of time.
///
/// <para>Separate fixture, <c>[Category("Integration")]</c> and <c>[NonParallelizable]</c>:
/// it spawns <c>dotnet publish</c>, which takes ~10-30 s and wants the machine to itself.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class NetShimGeneratorPublishTests
{
    private static NetParameterDescriptor P(string type, NetRefKind refKind = NetRefKind.None) =>
        new(refKind, type);

    private static NetMemberDescriptor Member(
        string name, string declaringType, NetMemberCategory kind, bool isStatic,
        string type, params NetParameterDescriptor[] parameters) =>
        new(name, declaringType, kind, isStatic, 0, type, parameters);

    /// <summary>
    /// One member per emission path that can fail to COMPILE, all of them over BCL types a
    /// hand-fed surface can legitimately name: a bool result, a String result and parameter, a
    /// constructor (handle result), a value-type receiver reached through
    /// <c>Unsafe.Unbox&lt;T&gt;</c>, an ENUM result (which is a handle by §8.3's default row and
    /// is the case that breaks a naive <c>rv is null</c> encode with CS0037), a void static with
    /// a scalar parameter, and an <c>out</c> scalar slot.
    /// </summary>
    private static NetSurface PublishableSurface() => new(
        new[]
        {
            Member("IsMatch", "System.Text.RegularExpressions.Regex", NetMemberCategory.Method,
                   false, "System.Boolean", P("System.String")),
            Member("Escape", "System.Text.RegularExpressions.Regex", NetMemberCategory.Method,
                   true, "System.String", P("System.String")),
            Member(".ctor", "System.Text.RegularExpressions.Regex", NetMemberCategory.Constructor,
                   false, "System.Void", P("System.String")),
            Member("AddDays", "System.DateTime", NetMemberCategory.Method,
                   false, "System.DateTime", P("System.Double")),
            Member("Options", "System.Text.RegularExpressions.Regex", NetMemberCategory.Property,
                   false, "System.Text.RegularExpressions.RegexOptions"),
            Member("Sleep", "System.Threading.Thread", NetMemberCategory.Method,
                   true, "System.Void", P("System.Int32")),
            Member("TryParse", "System.Int32", NetMemberCategory.Method,
                   true, "System.Boolean", P("System.String"), P("System.Int32", NetRefKind.Out)),
        },
        Array.Empty<string>());

    [Test]
    public void GeneratedShimPublishesUnderNativeAot()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("The shim publish recipe is pinned to win-x64 (spec §8.1).");

        // OUTSIDE the repo on purpose: a generated shim normally lands under the user's
        // obj/gen/shim, and a Directory.Build.props anywhere above it would silently rewrite the
        // §8.1 properties this test exists to prove.
        var dir = Path.Combine(Path.GetTempPath(), "blnet-gen-shim-" + Guid.NewGuid().ToString("N"));
        try
        {
            var written = NetShimGenerator.WriteTo(
                dir, PublishableSurface(), "ProbeApp",
                valueTypeReceiverNames: new[] { "System.DateTime" });

            var csproj = written.Single(p => p.EndsWith(".csproj", StringComparison.Ordinal));
            var result = NetShimPublisher.Publish(csproj, Path.Combine(dir, "publish"), dir);

            Assert.That(result.Success, Is.True,
                "A shim generated from a hand-fed surface did not publish. This is the only test "
                + "that proves NetShimGenerator emits real C# rather than plausible-looking text; "
                + "read the compiler output below and fix the emission.\n" + result.Output);
            Assert.That(File.Exists(result.DllPath), Is.True, result.DllPath);

            TestContext.Out.WriteLine("Generated shim published to " + result.DllPath);
            foreach (var line in result.Output.Split('\n')
                         .Where(l => l.Contains("warning", StringComparison.OrdinalIgnoreCase)))
                TestContext.Out.WriteLine("PUBLISH WARNING: " + line.Trim());

            // A C# compiler warning from a GENERATED file is always the generator's defect —
            // this surface calls nothing obsolete and nothing ambiguous, so csc has nothing else
            // to complain about. Caught a real one on first run: the *.g.cs naming made Roslyn
            // treat the scaffolding as auto-generated, which silently DISABLED every nullable
            // annotation in it (8 x CS8669).
            //
            // Deliberately NOT asserted: "warning IL". Spec §12.3 inverts that assertion for
            // generated shims — an AOT/trim warning here is an INPUT to BL6020 (Task 16), so the
            // identical string means "build failure" for the hand shim and "diagnostic to map"
            // for this one.
            Assert.That(result.Output, Does.Not.Contain("warning CS"),
                "The generated shim compiled with C# warnings. Fix NetShimGenerator's emission — "
                + "a warning in generated code is noise the user cannot act on, and Task 16's "
                + "AotDiagnosticMapper has to sift real IL warnings out of it.\n" + result.Output);
        }
        finally { if (Directory.Exists(dir)) TryDelete(dir); }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, true); } catch (IOException) { /* publish output can stay locked */ }
    }
}
