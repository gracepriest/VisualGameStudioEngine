using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.Net;
using BasicLang.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// <see cref="NetProxyEmitter"/> and <see cref="NetSurface"/> — the pure, fast half. The
/// compile-and-run half is <see cref="NetProxyEmitterCompileTests"/>, a separate fixture so
/// the string pins never pay for a C++ toolchain probe.
///
/// <para><b>The load-bearing test in this file is
/// <see cref="EmptySurfaceWritesNoFileAtAll"/>.</b> Every project in the repo has an empty
/// surface, so "an empty surface emits nothing" is the entire native-side content of P2a-1's
/// inertness claim. It is asserted on the FILE SYSTEM — not on a returned collection being
/// empty — because the thing that would break an existing build is a file appearing in
/// <c>obj/gen</c>, and even an empty directory created "harmlessly" is a new build artifact
/// for every project that has never mentioned .NET.</para>
///
/// <para><b>Fixture surfaces are built per call, never cached in a static.</b>
/// <see cref="NetSurface"/> is a record over lists, so its <c>==</c> is reference equality
/// (its own remarks say so) — a shared instance would let an accidental
/// <c>Is.EqualTo(surface)</c> pass for the wrong reason.</para>
/// </summary>
[TestFixture]
public class NetProxyEmitterTests
{
    internal const string Module = "ProbeApp.Net.dll";

    // ---- Fixture surfaces ---------------------------------------------------------------

    private static NetParameterDescriptor P(string type, NetRefKind refKind = NetRefKind.None) =>
        new(refKind, type);

    private static NetMemberDescriptor Member(
        string name, string declaringType, NetMemberCategory kind, bool isStatic,
        string type, params NetParameterDescriptor[] parameters) =>
        new(name, declaringType, kind, isStatic, 0, type, parameters);

    /// <summary>
    /// THE cross-emitter drift oracle's surface: one member per wire shape the emitters
    /// distinguish — a bool return, a String return, a handle return via a constructor, a void
    /// return, a ByRef scalar, a property (handle-shaped enum result), and the §6.4 conversion
    /// pairs. A single-member surface would leave most of the emitter unexecuted while still
    /// passing every "does it emit the artifacts" assertion.
    ///
    /// <para><b>Every §8.3/§6.4 row this fixture omits is a row the drift oracle is BLIND to.</b>
    /// <c>NetShimGeneratorTests.ExportSignaturesMatchTheProxyTableSlotSignatures</c> is the only
    /// thing holding the C and C# wire tables together, and it can only compare the shapes this
    /// surface actually contains — so the DateTime/TimeSpan rows Task 7a added went unchecked
    /// until Task 8's Step 0 put them here. Adding a row to
    /// <c>NetMarshalTable.WireRows</c> without adding a member here re-opens exactly that gap.
    /// (Renamed from <c>SixShapeSurface</c> in Task-8 Step 0: the count is a moving target as
    /// §8.3 rows land, and <see cref="ShapeCount"/> is what keeps it honest.)</para>
    /// </summary>
    internal static NetSurface WireShapeSurface() => new(
        new[]
        {
            Member("IsMatch", "System.Text.RegularExpressions.Regex", NetMemberCategory.Method,
                   false, "System.Boolean", P("System.String")),
            Member("Escape", "System.Text.RegularExpressions.Regex", NetMemberCategory.Method,
                   true, "System.String", P("System.String")),
            Member(".ctor", "System.Text.RegularExpressions.Regex", NetMemberCategory.Constructor,
                   false, "System.Void", P("System.String")),
            Member("Clear", "System.Collections.Generic.List<T>", NetMemberCategory.Method,
                   false, "System.Void"),
            Member("Advance", "MyLib.Counter", NetMemberCategory.Method,
                   false, "System.Void", P("System.Int32", NetRefKind.Ref)),
            Member("Options", "System.Text.RegularExpressions.Regex", NetMemberCategory.Property,
                   false, "System.Text.RegularExpressions.RegexOptions"),
            // §6.4's single-slot conversion pairs (P2a-2 Task 7a). Both DIRECTIONS on each:
            // the wire scalar appears as a parameter and as a result, which is what makes the
            // signature oracle compare CsParamType and CsOutType rather than only one of them.
            Member("AddDays", "System.DateTime", NetMemberCategory.Method,
                   false, "System.DateTime", P("System.Double")),
            Member("Subtract", "System.DateTime", NetMemberCategory.Method,
                   false, "System.TimeSpan", P("System.DateTime")),
            Member("Add", "System.TimeSpan", NetMemberCategory.Method,
                   false, "System.TimeSpan", P("System.TimeSpan")),
            // The NINE §8.3 by-value rows the fixture omitted until the Task-8 review — every
            // one of them invisible to the C-vs-C# signature oracle, including System.Char,
            // the row Task 8 itself worked on. One member carries them all as parameters; the
            // Char RESULT direction is carried by the member below, because a result travels
            // through CsOutType and a parameter through CsParamType and Char is the row where
            // the managed spelling (`char`) differs from BOTH (`ushort` on the wire).
            Member("EveryScalarRow", "MyLib.Widen", NetMemberCategory.Method,
                   true, "System.Void",
                   P("System.SByte"), P("System.Byte"), P("System.Int16"), P("System.UInt16"),
                   P("System.UInt32"), P("System.Int64"), P("System.UInt64"), P("System.Single"),
                   P("System.Char")),
            Member("ToChar", "MyLib.Widen", NetMemberCategory.Method,
                   true, "System.Char", P("System.Int32")),
        },
        new[] { "System.Text.RegularExpressions.Regex" });

    /// <summary>
    /// How many distinct slots <see cref="WireShapeSurface"/> contributes. Named rather than
    /// inlined so a fixture change updates ONE number and every count guard follows — a guard
    /// that silently tracked the fixture would assert nothing.
    /// </summary>
    internal const int ShapeCount = 11;

    /// <summary>
    /// <b>The row-by-row tie between <c>NetMarshalTable.WireRows</c>' <c>CWire</c> column and
    /// the C types <see cref="NetProxyEmitter"/> actually declares.</b> Three encodings of §8.3
    /// exist (the row table, the C emitter, the C# emitter); the signature oracle in
    /// <c>NetShimGeneratorTests</c> holds the two EMITTERS together, and this holds the row
    /// table to the C one — otherwise the call site could declare a ByRef temporary of a type
    /// the proxy never took.
    ///
    /// <para>Asserted over <see cref="WireShapeSurface"/>, which is why that fixture carrying
    /// every row matters twice over.</para>
    /// </summary>
    [Test]
    public void EveryWireRowsCTypeMatchesTheEmittedSlot()
    {
        var bindings = N(NetProxyEmitter.EmitBindings(WireShapeSurface()).Text);
        var covered = BasicLang.Net.NetMarshalTable.WireRows.Values
            .Where(r => !string.IsNullOrEmpty(r.CWire))
            .ToList();

        Assert.Multiple(() =>
        {
            foreach (var row in covered)
            {
                Assert.That(bindings, Does.Contain(row.CWire + " "),
                    $"NetMarshalTable's row for '{row.NetFullName}' declares its wire slot as "
                    + $"'{row.CWire}', but no slot in the emitted BlnetProxyTable is spelled "
                    + "that way. The call site declares a ByRef temporary of exactly this type "
                    + "(§8.3's pointer slot), so a disagreement here is a temporary that either "
                    + "fails to bind or silently changes width. Fix whichever side is wrong — "
                    + "they are two separate tables of the same spec rows.");
            }
        });
    }

    internal static NetSurface OneMemberSurface() => new(
        new[]
        {
            Member("IsMatch", "System.Text.RegularExpressions.Regex", NetMemberCategory.Method,
                   false, "System.Boolean", P("System.String")),
        },
        Array.Empty<string>());

    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "blnet-emit-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Normalizes line endings on BOTH sides of every text comparison. <c>core.autocrlf</c> is
    /// true here and <c>.gitattributes</c> says <c>* text=auto</c>, so the verbatim strings
    /// spliced out of <c>BlnetRuntimeSources.cs</c> carry CRLF while the emitter's own output
    /// is LF by construction.
    /// </summary>
    private static string N(string s) => s.Replace("\r\n", "\n");

    // ---- THE inertness property ---------------------------------------------------------

    /// <summary>
    /// <b>Mutation-tested during Task 12:</b> making <see cref="NetProxyEmitter.WriteTo"/>
    /// write one file unconditionally turned this red on both assertions. If it can ever pass
    /// with an unconditional write, it is worthless and the task's whole claim is unguarded.
    /// </summary>
    [Test]
    public void EmptySurfaceWritesNoFileAtAll()
    {
        var dir = TempDir();
        try
        {
            var written = NetProxyEmitter.WriteTo(dir, NetSurface.Empty, Module);

            Assert.Multiple(() =>
            {
                Assert.That(written, Is.Empty,
                    "NetProxyEmitter.WriteTo reported writing files for an EMPTY .NET surface. "
                    + "Every project in this repo has an empty surface, so this is P2a-1 changing "
                    + "the output of programs it must not touch. Fix NetProxyEmitter — emission "
                    + "must stay inside the surface.IsNonEmpty branch.");
                Assert.That(Directory.Exists(dir), Is.False,
                    "NetProxyEmitter.WriteTo CREATED obj/gen for an empty .NET surface. Even an "
                    + "empty directory is a new build artifact for every existing project. "
                    + "Directory.CreateDirectory belongs inside the non-empty branch, not as a "
                    + "precondition at the top of WriteTo.");
            });
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Test]
    public void EmptySurfaceEmitsNoArtifactsAndNoTranslationUnits()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NetProxyEmitter.Emit(NetSurface.Empty, Module), Is.Empty,
                "An empty surface must produce an EMPTY artifact set — CppProjectBuilder merges "
                + "this dictionary into obj/gen, so a single entry here reaches every project.");
            Assert.That(NetProxyEmitter.TranslationUnitFileNames(NetSurface.Empty), Is.Empty,
                "An empty surface must contribute no translation units — one here would join "
                + "request.SourceFiles and be compiled into every existing native project.");
            Assert.That(NetSurface.Empty.IsNonEmpty, Is.False);
        });
    }

    /// <summary>
    /// A <c>&lt;NetProxy&gt;</c> declaration with no expanded members still counts as .NET use.
    /// Documented on <see cref="NetSurface.IsNonEmpty"/>; pinned here because the alternative
    /// reading (members only) silently drops the shim for a C++ project whose declared type
    /// expanded to nothing — a build that looks clean and does nothing.
    /// </summary>
    [Test]
    public void DeclarationsAloneMakeTheSurfaceNonEmpty()
    {
        var surface = new NetSurface(Array.Empty<NetMemberDescriptor>(), new[] { "MyLib.Customer" });

        Assert.Multiple(() =>
        {
            Assert.That(surface.IsNonEmpty, Is.True);
            Assert.That(NetProxyEmitter.Emit(surface, Module).Keys, Is.EquivalentTo(ExpectedArtifacts));
        });
    }

    // ---- §9.1's artifact set ------------------------------------------------------------

    /// <summary>
    /// §9.1's <c>obj/gen</c> FILE entries plus <c>blnet_marshal.hpp</c> (§6.4's conversion
    /// pairs, P2a-2 Task 6 — constant text, surface-keyed like the rest of the set). The
    /// <c>shim/</c> DIRECTORY belongs to phase 5 (<c>NetShimGenerator</c>, plan Task 14)
    /// and is deliberately not produced here — §10.1 requires phases 1-4 to give full C++
    /// IntelliSense without ever publishing a shim.
    /// </summary>
    internal static readonly string[] ExpectedArtifacts =
    {
        "blnet.h", "blnet_runtime.hpp", "blnet_marshal.hpp", "blnet_bindings.g.hpp",
        "blnet_proxies.g.hpp", "blnet_startup.g.cpp",
    };

    [Test]
    public void NonEmptySurfaceEmitsExactlyTheSixNativeArtifacts()
    {
        var files = NetProxyEmitter.Emit(WireShapeSurface(), Module);

        Assert.That(files.Keys, Is.EquivalentTo(ExpectedArtifacts),
            "The obj/gen artifact set drifted from spec §9.1. If a file was added or renamed, "
            + "update NetProxyEmitter's *FileName constants AND CppProjectBuilder's merge (plan "
            + "Task 13 keys request.SourceFiles and the include path off this set).");
    }

    [Test]
    public void WriteToPutsExactlyThoseFilesOnDisk()
    {
        var dir = TempDir();
        try
        {
            var written = NetProxyEmitter.WriteTo(dir, WireShapeSurface(), Module);

            Assert.Multiple(() =>
            {
                Assert.That(written.Select(Path.GetFileName), Is.EquivalentTo(ExpectedArtifacts));
                Assert.That(Directory.GetFiles(dir).Select(Path.GetFileName),
                    Is.EquivalentTo(ExpectedArtifacts),
                    "WriteTo left obj/gen holding something other than the artifact set.");
            });
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Test]
    public void OnlyTheStartupFileIsATranslationUnit()
    {
        Assert.That(NetProxyEmitter.TranslationUnitFileNames(WireShapeSurface()),
            Is.EqualTo(new[] { "blnet_startup.g.cpp" }),
            "The generated .NET artifact set has exactly one TU. If that changes, plan Task 13's "
            + "generatedTus union must change with it or the new TU is emitted and never linked.");
    }

    /// <summary>
    /// The two P0 headers are SPLICED, never re-authored. A hand-maintained copy here would be
    /// a second source of truth for the ABI, and the drift would surface only as a mismatched
    /// struct layout at run time.
    /// </summary>
    [Test]
    public void ContractHeadersAreSplicedVerbatimFromBlnetRuntimeSources()
    {
        var files = NetProxyEmitter.Emit(WireShapeSurface(), Module);

        Assert.Multiple(() =>
        {
            Assert.That(files["blnet.h"], Is.EqualTo(BlnetRuntimeSources.BlnetHeader),
                "blnet.h must be BlnetRuntimeSources.BlnetHeader verbatim — fix NetProxyEmitter, "
                + "never by editing a copy of the header.");
            Assert.That(files["blnet_runtime.hpp"], Is.EqualTo(BlnetRuntimeSources.BlnetRuntime),
                "blnet_runtime.hpp must be BlnetRuntimeSources.BlnetRuntime verbatim.");
        });
    }

    // ---- §9.2: call scope + null-slot guard, in EVERY proxy -----------------------------

    /// <summary>
    /// §9.2 makes both normative for every proxy, and both fail SILENTLY rather than loudly:
    /// a missing <c>BlnetCallScope</c> misdispatches every delegate argument (a result-bearing
    /// one fails <c>BLNET_E_CROSS_THREAD_RESULT</c>, an <c>Action</c> is queued instead of run),
    /// and a missing slot guard is a jump through a null function pointer. Checked per proxy
    /// BODY, not per file: one scope anywhere in the header would satisfy a whole-file
    /// <c>Does.Contain</c> while five of six proxies were broken.
    /// </summary>
    [Test]
    public void EveryProxyBodyHoldsACallScopeAndGuardsItsSlot()
    {
        var surface = WireShapeSurface();
        var proxies = N(NetProxyEmitter.Emit(surface, Module)["blnet_proxies.g.hpp"]);
        var slots = NetProxyEmitter.EmitBindings(surface).SlotNames;

        Assert.That(slots, Has.Count.EqualTo(ShapeCount), "the fixture surface lost a shape");

        Assert.Multiple(() =>
        {
            foreach (var slot in slots)
            {
                var body = ProxyBody(proxies, slot);
                Assert.That(body, Is.Not.Null,
                    $"No proxy function was emitted for slot '{slot}'. Every table slot needs a "
                    + "typed proxy — the slot alone is not the public API (§9.2).");
                Assert.That(body, Does.Contain("BasicLang::blnet::BlnetCallScope"),
                    $"The proxy for '{slot}' calls through g_net WITHOUT a BlnetCallScope. §9.2 "
                    + "makes it normative: P0's thunk treats g_call_depth == 0 as cross-thread, so "
                    + "without the scope every delegate argument misdispatches — silently, for an "
                    + "Action. Fix NetProxyEmitter.EmitProxyBody.");
                Assert.That(body, Does.Contain($"BlnetRequireSlot(g_net.{slot} != nullptr"),
                    $"The proxy for '{slot}' does not guard its slot. §9.2 requires a clear "
                    + "diagnostic when blnet_startup() has not run (a static-initialization-order "
                    + "violation) instead of a null function-pointer jump.");
                // P2a-2 Task 7a: proxies call the TYPED check — NetCheckTyped throws
                // BasicLang::NetException carrying the shim-reported inheritance chain
                // (§11.1's ladder input); P0's NetCheck keeps its frozen composed-message
                // shape for hand-written hosts.
                Assert.That(body, Does.Contain("BasicLang::blnet::NetCheckTyped(blnet_status);"),
                    $"The proxy for '{slot}' drops the returned status on the floor.");
            }
        });
    }

    /// <summary>
    /// The scope must CLOSE before <c>NetCheck</c> runs: <c>NetCheck</c> throws, and unwinding
    /// through a still-counted scope corrupts P0's depth counter for the rest of that thread's
    /// life, after which every later callback misdispatches. Ordering, not presence — which is
    /// why this is separate from the test above.
    /// </summary>
    [Test]
    public void NetCheckSitsOutsideTheCallScope()
    {
        var surface = WireShapeSurface();
        var proxies = N(NetProxyEmitter.Emit(surface, Module)["blnet_proxies.g.hpp"]);

        Assert.Multiple(() =>
        {
            foreach (var slot in NetProxyEmitter.EmitBindings(surface).SlotNames)
            {
                var body = ProxyBody(proxies, slot)!;
                var scopeEnd = body.IndexOf("\n    }\n", StringComparison.Ordinal);
                // Task 7a spelling: proxies call NetCheckTyped (the §11.1 typed conversion).
                var check = body.IndexOf("NetCheckTyped(blnet_status);", StringComparison.Ordinal);
                Assert.That(scopeEnd, Is.GreaterThan(-1), $"proxy '{slot}' has no scope block");
                Assert.That(check, Is.GreaterThan(scopeEnd),
                    $"The proxy for '{slot}' calls NetCheckTyped INSIDE the BlnetCallScope "
                    + "block. NetCheckTyped throws; unwinding through the scope's destructor "
                    + "while it is still counted corrupts g_call_depth permanently for that "
                    + "thread (§9.2).");
            }
        });
    }

    /// <summary>
    /// §15.12: pump at the OUTERMOST boundary return only. A pump in every nested proxy would
    /// re-enter and fail <c>BLNET_E_PUMP_REENTRY</c>; no pump at all silently strands every
    /// notification a foreign thread queued during the call.
    /// </summary>
    [Test]
    public void ProxiesPumpOnlyAtTheOutermostReturn()
    {
        var surface = WireShapeSurface();
        var proxies = N(NetProxyEmitter.Emit(surface, Module)["blnet_proxies.g.hpp"]);

        Assert.Multiple(() =>
        {
            foreach (var slot in NetProxyEmitter.EmitBindings(surface).SlotNames)
                Assert.That(ProxyBody(proxies, slot),
                    Does.Contain("if (BasicLang::blnet::g_call_depth == 0) "
                                 + "(void)BasicLang::blnet::blnet_pump();"),
                    $"The proxy for '{slot}' does not honor spec open item 15.12's decided "
                    + "behavior: pump after NetCheck, guarded on the call depth having returned "
                    + "to 0.");
        });
    }

    // ---- Cross-artifact consistency -----------------------------------------------------

    /// <summary>
    /// Three artifacts name the same slots — the table declares them, the startup TU binds them
    /// and the proxy header defines a function per slot. They come out of three separate emit
    /// methods, so nothing but this stops one of them being changed alone; the drift is a link
    /// error at best and a permanently-null slot at worst.
    /// </summary>
    [Test]
    public void BindingsStartupAndProxiesAgreeOnEverySlotName()
    {
        var surface = WireShapeSurface();
        var files = NetProxyEmitter.Emit(surface, Module);
        var bindings = N(files["blnet_bindings.g.hpp"]);
        var startup = N(files["blnet_startup.g.cpp"]);
        var proxies = N(files["blnet_proxies.g.hpp"]);

        Assert.Multiple(() =>
        {
            foreach (var slot in NetProxyEmitter.EmitBindings(surface).SlotNames)
            {
                Assert.That(bindings, Does.Contain($"int32_t (BLNET_CALL *{slot})("),
                    $"blnet_bindings.g.hpp declares no slot '{slot}'.");
                Assert.That(startup, Does.Contain($"g_net.{slot} = reinterpret_cast<"),
                    $"blnet_startup.g.cpp's blnet_bind_all never binds '{slot}', so that slot "
                    + "stays null forever and every call through it hits the §9.2 guard.");
                Assert.That(startup, Does.Contain($"blnet_get_symbol(module, \"{slot}\")"),
                    $"blnet_bind_all looks up a name other than '{slot}' — the export name and "
                    + "the slot name are the SAME string by design (§12.4).");
                Assert.That(proxies, Does.Contain($" {slot}("),
                    $"blnet_proxies.g.hpp defines no proxy for '{slot}'.");
            }
        });
    }

    /// <summary>
    /// §7.1's collector walks call sites, so the same member arrives once per call site. Three
    /// identically-named struct fields do not compile — and the shim generator must collapse on
    /// the same key or §12.4's slots-≡-exports comparison fails on a surface nobody thought to
    /// de-duplicate.
    /// </summary>
    [Test]
    public void RepeatedMembersCollapseToOneSlot()
    {
        var one = OneMemberSurface();
        var thrice = new NetSurface(
            one.Members.Concat(one.Members).Concat(one.Members).ToList(), Array.Empty<string>());

        Assert.That(NetProxyEmitter.EmitBindings(thrice).SlotNames,
            Is.EqualTo(NetProxyEmitter.EmitBindings(one).SlotNames),
            "A member reached from three call sites must still produce ONE proxy-table slot.");
    }

    // ---- §9.3's startup contract --------------------------------------------------------

    /// <summary>
    /// The four failure texts are normative — later tasks assert on them from outside the
    /// compiler, so a reworded message is an API break, not a cosmetic edit.
    /// </summary>
    [Test]
    public void StartupCarriesTheFourNormativeFailureMessages()
    {
        var startup = N(NetProxyEmitter.Emit(WireShapeSurface(), Module)["blnet_startup.g.cpp"]);

        Assert.Multiple(() =>
        {
            Assert.That(startup, Does.Contain("\"blnet: failed to load '\""));
            Assert.That(startup, Does.Contain("\"blnet: shim is missing export '\""));
            Assert.That(startup, Does.Contain("\"blnet: shim ABI \""));
            Assert.That(startup, Does.Contain("\"blnet: initialize failed (status \""));
            Assert.That(startup, Does.Contain("std::fprintf(stderr,"),
                "§9.3 puts every startup failure on stderr, not stdout.");
            Assert.That(startup, Does.Contain("std::exit(3);"),
                "§9.3 fixes the startup-failure exit code at 3.");
        });
    }

    [Test]
    public void StartupPerformsTheTwoArgumentHandshakeAgainstTheContractVersion()
    {
        var startup = N(NetProxyEmitter.Emit(WireShapeSurface(), Module)["blnet_startup.g.cpp"]);

        Assert.Multiple(() =>
        {
            Assert.That(startup, Does.Contain(
                "g_shim.initialize(\n        BLNET_ABI_VERSION, &BasicLang::blnet::g_native_vtable)"),
                "P0's frozen signature is initialize(int32_t expected_abi, const "
                + "BlnetNativeVtable*). A one-argument call compiles against nothing, and a null "
                + "vtable makes every callback unreachable.");
            Assert.That(startup, Does.Contain("abi != BLNET_ABI_VERSION"),
                "The ABI handshake must compare against the header's BLNET_ABI_VERSION, which is "
                + "generated from BlnetContract.AbiVersion — never a literal.");
            Assert.That(startup, Does.Contain($"\"{Module}\""),
                "The startup TU must load the shim module name it was given.");
        });
    }

    /// <summary>
    /// §9.5: one static-initializer object covers both an emitted <c>main</c> and a
    /// user-written one. Without it a hand-written C++ <c>main()</c> silently never starts the
    /// boundary and every proxy call hits the null-slot guard.
    /// </summary>
    [Test]
    public void StartupOwnershipIsAStaticInitializerObject()
    {
        var startup = N(NetProxyEmitter.Emit(WireShapeSurface(), Module)["blnet_startup.g.cpp"]);

        Assert.Multiple(() =>
        {
            Assert.That(startup, Does.Contain("BlnetStartupGuard()  { blnet_startup();  }"));
            Assert.That(startup, Does.Contain("~BlnetStartupGuard() { blnet_shutdown(); }"));
            Assert.That(startup, Does.Contain("BlnetStartupGuard g_blnet_startup_guard;"));
            Assert.That(startup, Does.Contain("static initialization order across translation units"),
                "The static-initialization-order constraint must be documented in the EMITTED "
                + "comment, not only in the C# that produced it — the generated file is what a "
                + "user debugging a null-slot error actually opens.");
        });
    }

    // ---- Refusals -----------------------------------------------------------------------

    /// <summary>
    /// §8.3 pins ByRef slots only for by-value scalars. Guessing at ByRef-handle ownership
    /// produces a double release, and ByRef String cannot carry the in direction through a
    /// <c>char**</c> at all — both are use-after-free in generated C++, so the emitter refuses
    /// rather than emitting something plausible.
    /// </summary>
    [TestCase("System.String")]
    [TestCase("System.Text.RegularExpressions.Regex")]
    public void ByRefNonScalarParametersAreRefusedLoudly(string parameterType)
    {
        var surface = new NetSurface(
            new[]
            {
                Member("Try", "MyLib.Thing", NetMemberCategory.Method, false, "System.Void",
                       P(parameterType, NetRefKind.Ref)),
            },
            Array.Empty<string>());

        var ex = Assert.Throws<NotSupportedException>(() => NetProxyEmitter.Emit(surface, Module));
        Assert.That(ex!.Message, Does.Contain("§8.3"));
    }

    [Test]
    public void ByRefScalarParametersAreSupported()
    {
        var surface = new NetSurface(
            new[]
            {
                Member("Advance", "MyLib.Counter", NetMemberCategory.Method, false, "System.Void",
                       P("System.Int32", NetRefKind.Ref)),
            },
            Array.Empty<string>());
        var slot = NetProxyEmitter.EmitBindings(surface).SlotNames.Single();
        var files = NetProxyEmitter.Emit(surface, Module);

        Assert.Multiple(() =>
        {
            Assert.That(N(files["blnet_bindings.g.hpp"]),
                Does.Contain($"int32_t (BLNET_CALL *{slot})(uint64_t self, int32_t* a0);"));
            Assert.That(ProxyBody(N(files["blnet_proxies.g.hpp"]), slot),
                Does.Contain("a0 = blnet_a0;"),
                "A ByRef scalar must be written back into the caller's variable.");
        });
    }

    // ---- Wire forms ---------------------------------------------------------------------

    /// <summary>
    /// §8.3's rows, read off the emitted C signature. A constructor is the row most easily got
    /// wrong: its metadata return type is <c>System.Void</c>, but its export hands back the
    /// object it created, so reading <c>TypeFullName</c> literally emits a slot that constructs
    /// and discards.
    /// </summary>
    [Test]
    public void WireFormsFollowTheMarshalingTable()
    {
        var surface = WireShapeSurface();
        var bindings = N(NetProxyEmitter.Emit(surface, Module)["blnet_bindings.g.hpp"]);
        var slots = NetProxyEmitter.EmitBindings(surface).SlotNames;

        Assert.Multiple(() =>
        {
            // Boolean travels as int32 (bool is not blittable for UnmanagedCallersOnly);
            // a String in-param borrows a const char*.
            Assert.That(bindings, Does.Contain(
                $"*{slots[0]})(uint64_t self, const char* a0, int32_t* result);"));
            // A String RESULT is a transfer buffer, not a borrowed pointer; a static member has
            // no receiver.
            Assert.That(bindings, Does.Contain($"*{slots[1]})(const char* a0, char** result);"));
            // A constructor: no receiver, and a HANDLE result despite System.Void metadata.
            Assert.That(bindings, Does.Contain($"*{slots[2]})(const char* a0, uint64_t* result);"));
            // Void return, instance member: receiver only.
            Assert.That(bindings, Does.Contain($"*{slots[3]})(uint64_t self);"));
            // Everything outside the table is a handle — here, an enum-typed property.
            Assert.That(bindings, Does.Contain($"*{slots[5]})(uint64_t self, uint64_t* result);"));
        });
    }

    // ---- Helpers ------------------------------------------------------------------------

    /// <summary>
    /// The text of one emitted proxy function: from its <c>inline</c> signature line up to the
    /// next one (or the namespace close). Per-function rather than whole-file so an assertion
    /// cannot be satisfied by a different proxy's body.
    /// </summary>
    private static string? ProxyBody(string proxies, string slot)
    {
        var start = proxies.IndexOf("\ninline ", StringComparison.Ordinal);
        while (start >= 0)
        {
            var next = proxies.IndexOf("\ninline ", start + 1, StringComparison.Ordinal);
            var end = next < 0 ? proxies.Length : next;
            var body = proxies.Substring(start, end - start);
            if (body.Contains(" " + slot + "(", StringComparison.Ordinal)) return body;
            start = next;
        }
        return null;
    }
}

/// <summary>
/// The emitted C++ handed to a real compiler. String pins cannot catch a missing semicolon,
/// an unresolved symbol, or a static initializer that terminates instead of exiting — these
/// can.
///
/// <para>Separate fixture (rather than <c>[Category("Integration")]</c> on three methods of
/// <see cref="NetProxyEmitterTests"/>) so the compiler probe runs only when the Integration
/// tests do: a <c>[OneTimeSetUp]</c> is per FIXTURE, and putting it beside the string pins
/// would make the fast subset spawn vswhere.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class NetProxyEmitterCompileTests
{
    private (string exe, string argsTemplate)? _compiler;

    [OneTimeSetUp]
    public void FindCompiler() => _compiler = Native.CppCompile.FindRunCompiler();

    private static Dictionary<string, string> ArtifactsPlus(
        NetSurface surface, string mainSource, string module = NetProxyEmitterTests.Module)
    {
        var files = NetProxyEmitter.Emit(surface, module).ToDictionary(kv => kv.Key, kv => kv.Value);
        files["main.cpp"] = mainSource;
        return files;
    }

    /// <summary>
    /// The real proof the emitted C++ is valid: a compiler parses the generated headers with a
    /// six-shape surface in them. Inline functions are fully checked whether or not they are
    /// called, so every proxy body is covered by this one compile.
    /// </summary>
    [Test]
    public void EmittedHeadersCompileStandalone()
    {
        if (_compiler is null) Assert.Ignore("No C++ compiler available");

        var files = ArtifactsPlus(NetProxyEmitterTests.WireShapeSurface(), """
            #include "blnet_proxies.g.hpp"
            #include <cstdio>
            BlnetProxyTable g_net{};
            int main() { std::printf("ok"); return 0; }
            """);

        Assert.That(
            Native.CppCompile.CompileAndRunFiles(files, new[] { "main.cpp" }, _compiler!.Value),
            Is.EqualTo("ok"));
    }

    /// <summary>
    /// The §9.2 guard, executed rather than grepped: with <c>g_net</c> defined but never bound
    /// — exactly the state a static-initialization-order violation leaves it in — a proxy call
    /// must raise a readable error naming the slot instead of jumping through a null pointer.
    ///
    /// <para><c>g_net</c> is defined by the test TU rather than by linking
    /// <c>blnet_startup.g.cpp</c>, deliberately: linking startup would run the static
    /// initializer, fail to load a shim that does not exist, and exit 3 before this scenario
    /// could happen at all.</para>
    /// </summary>
    [Test]
    public void UnboundSlotRaisesTheGuardInsteadOfCrashing()
    {
        if (_compiler is null) Assert.Ignore("No C++ compiler available");

        var surface = NetProxyEmitterTests.OneMemberSurface();
        var slot = NetProxyEmitter.EmitBindings(surface).SlotNames.Single();
        var files = ArtifactsPlus(surface, $$"""
            #include "blnet_proxies.g.hpp"
            #include <cstdio>
            BlnetProxyTable g_net{};   /* every slot null: startup never ran */
            int main() {
                try {
                    (void)BasicLang::net::{{slot}}(BasicLang::blnet::NetRef(), "x");
                    std::printf("guard=NONE\n");
                } catch (const std::exception& ex) {
                    std::printf("guard=CAUGHT\n%s\n", ex.what());
                }
                return 0;
            }
            """);

        var output = Native.CppCompile.CompileAndRunFiles(files, new[] { "main.cpp" }, _compiler!.Value);

        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Contain("guard=CAUGHT"),
                "Calling an unbound proxy slot did NOT raise §9.2's guard. Without it this is a "
                + "call through a null function pointer.");
            Assert.That(output, Does.Contain(slot),
                "The guard fired but did not name the slot, which is the only actionable part of "
                + "the message.");
            Assert.That(output, Does.Contain("blnet_startup() has not run"),
                "The guard must name the likely cause — a static-initialization-order violation.");
        });
    }

    /// <summary>
    /// The startup translation unit, compiled and RUN. The only test that proves the generated
    /// <c>blnet_startup.g.cpp</c> links at all (the platform loader, <c>blnet_bind_core</c>,
    /// <c>g_native_vtable</c>, the static-initializer object) and that §9.3's failure protocol
    /// is real: the message on stderr, exit 3, and — because the guard is a STATIC initializer
    /// — before <c>main</c> ever runs.
    /// </summary>
    [Test]
    public void StartupTuFailsTheDocumentedWayWhenTheShimIsMissing()
    {
        if (_compiler is null) Assert.Ignore("No C++ compiler available");

        const string missing = "NoSuchBlnetShim.Net.dll";
        var files = ArtifactsPlus(NetProxyEmitterTests.WireShapeSurface(), """
            #include <cstdio>
            int main() { std::printf("reached-main"); return 0; }
            """, missing);

        var outcome = Native.CppCompile.CompileAndRunFilesForOutcome(
            files, new[] { "blnet_startup.g.cpp", "main.cpp" }, _compiler!.Value);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.StdErr, Does.Contain($"blnet: failed to load '{missing}'"),
                "§9.3's module-not-found message must reach stderr verbatim. Actual stderr: "
                + outcome.StdErr);
            Assert.That(outcome.ExitCode, Is.EqualTo(3),
                "§9.3 fixes the startup-failure exit code at 3. A different code usually means "
                + "the failure escaped the static initializer as an exception (std::terminate), "
                + "which loses both the code and the message.");
            Assert.That(outcome.StdOut, Does.Not.Contain("reached-main"),
                "The boundary must fail at STARTUP, before user code runs — that is the whole "
                + "point of the static-initializer object (§9.5).");
        });
    }
}
