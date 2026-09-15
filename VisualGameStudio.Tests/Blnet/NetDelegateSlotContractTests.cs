using System;
using System.Linq;
using BasicLang.Compiler.CodeGen.Net;
using BasicLang.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// §8.4's <b>slot marshaling contract</b>, resolved 2026-09-15 — the delegate-shaped twin of
/// §8.3's ByRef handle resolution, and found the same way.
///
/// <para><b>What the gap actually was.</b> §8.4's prose named three new pieces and said
/// "everything else already exists and is tested"; it never wrote down which slot SHAPES a
/// callback may carry. That restriction lived only in code comments — "v1 admits blittable
/// scalars only" — so three gates refused handles and strings on the strength of a comment.
/// Meanwhile the native runtime had already implemented its half: <c>BLNET_SLOT_HANDLE</c>
/// addrefs at enqueue and the pump releases, <c>BLNET_SLOT_STRING</c> deep-copies. The shapes
/// were transportable; nothing had specified the ownership.</para>
///
/// <para><b>Measured, not assumed.</b> Across the 38 <c>&lt;NetProxy&gt;</c> types the wiki
/// documents, exactly five slots were refused: a handle parameter on
/// <c>Action&lt;Task&gt;</c> and on <c>MatchEvaluator</c>, a handle return on
/// <c>Func&lt;Task&gt;</c>, a String return on <c>MatchEvaluator</c>, and
/// <c>ParameterizedThreadStart</c>'s <c>System.Object</c>. The first four are this contract;
/// the fifth stays refused forever, and <see cref="ObjectIsRefusedAheadOfTheHandleRule"/> is
/// why that is not an accident.</para>
/// </summary>
[TestFixture]
public class NetDelegateSlotContractTests
{
    // ====================================================================================
    // 1. The classifier — ONE verdict, three consumers.
    // ====================================================================================

    [TestCase("System.Int32", NetSlotKindName.Value)]
    [TestCase("System.Double", NetSlotKindName.Value)]
    [TestCase("System.String", NetSlotKindName.String)]
    [TestCase("System.Threading.Tasks.Task", NetSlotKindName.Handle)]
    [TestCase("System.Text.RegularExpressions.Match", NetSlotKindName.Handle)]
    [TestCase("Contoso.AnythingWithNoRow", NetSlotKindName.Handle)]
    public void TheClassifierAdmitsScalarsHandlesAndStrings(string typeFullName, string expected)
    {
        Assert.That(NetDelegateDispatchAccess.TryClassify(typeFullName, out var kind, out var refusal),
            Is.True, "should be admissible, but: " + refusal);
        Assert.That(kind, Is.EqualTo(expected));
    }

    /// <summary>
    /// <c>System.Object</c> has no marshal row, and "no row" IS the handle rule — so without an
    /// arm of its own it would be admitted as a handle by the very change that admits
    /// <c>Task</c>. §8.3 rejects it permanently (<c>void*</c> erasure is unsound), so the arm
    /// has to run FIRST, and this is the test that fails if someone later tidies it away as
    /// redundant.
    /// </summary>
    [Test]
    public void ObjectIsRefusedAheadOfTheHandleRule()
    {
        Assert.That(
            NetDelegateDispatchAccess.TryClassify("System.Object", out _, out var refusal),
            Is.False,
            "System.Object must NOT ride the no-marshal-row handle rule — §8.3 rejects it in "
            + "every position, and the handle rule would otherwise admit it silently.");

        Assert.That(refusal, Does.Contain("permanently"),
            "and the refusal must say permanently, so nobody reads it as another not-yet: "
            + refusal);
    }

    [TestCase("System.Boolean", "wire spelling")]
    [TestCase("System.Char", "wire spelling")]
    [TestCase("System.Guid", "§6.4")]
    [TestCase("System.Decimal", "§6.4")]
    public void WhatStaysRefused_SaysItsOwnReason(string typeFullName, string expected)
    {
        Assert.That(NetDelegateDispatchAccess.TryClassify(typeFullName, out _, out var refusal),
            Is.False);
        Assert.That(refusal, Does.Contain(expected),
            "each refused shape is a DIFFERENT open question and must read as one — widening by "
            + "analogy with the handle row is exactly what §8.3 and §8.4 both forbid: " + refusal);
    }

    // ====================================================================================
    // 2. The slot descriptors — the one disagreement that fails SILENTLY.
    // ====================================================================================

    /// <summary>
    /// <b>The highest-value assertion in this fixture.</b> <c>BlnetSlotDesc[]</c> is what
    /// <c>blnet_invoke_callback</c> consults when a callback is QUEUED instead of run inline:
    /// HANDLE makes it addref at enqueue, STRING makes it deep-copy, VALUE makes it do neither.
    ///
    /// <para>Label a handle slot VALUE and everything still compiles, links, and passes every
    /// inline test — then the managed dispatcher's <c>finally</c> releases the only reference
    /// and the object can be collected before the pump ever runs the callback. There is no
    /// compile error anywhere on that path, which is why the descriptors are asserted per kind
    /// rather than left to follow from the code that produces them.</para>
    /// </summary>
    [Test]
    public void SlotDescriptorsCarryTheRealKind_NotAlwaysValue()
    {
        var descriptors = NetDelegateDispatch.CppSlotDescriptors(
            "System.Void(System.Int32,System.String,System.Threading.Tasks.Task)");

        Assert.That(descriptors, Is.EqualTo(
            "{ {BLNET_SLOT_VALUE, 0}, {BLNET_SLOT_STRING, 0}, {BLNET_SLOT_HANDLE, 0} }"),
            "each slot must carry ITS OWN kind. All-VALUE compiles and passes every inline "
            + "test, and then drops the enqueue addref and the deep copy on the queued path.");
    }

    [Test]
    public void AZeroArgDelegateStillDescribesNoSlots()
    {
        Assert.That(NetDelegateDispatch.CppSlotDescriptors("System.Void()"), Is.Null,
            "blnet_register_callback guards argc == 0 explicitly, so an empty array would be "
            + "pretend-work.");
    }

    // ====================================================================================
    // 3. The managed dispatcher — ownership, asserted in the emitted text.
    // ====================================================================================

    /// <summary>
    /// A handle argument is minted by <c>ToHandle</c> (which is <c>Table.Create</c>, so always a
    /// FRESH handle) and released in a <c>finally</c>.
    ///
    /// <para>The <c>finally</c> is not tidiness. The thunk reports a native failure as a status,
    /// but a managed exception thrown by the callback body unwinds straight through this frame —
    /// and the leak there is one table slot per invocation, on a callback that may be invoked in
    /// a loop.</para>
    /// </summary>
    [Test]
    public void AHandleArgument_IsMintedFresh_AndReleasedEvenWhenTheCallbackThrows()
    {
        var exports = Exports(HandleParamSurface());

        Assert.Multiple(() =>
        {
            Assert.That(exports, Does.Contain("args_[0] = ToHandle(a0);"),
                "ToHandle mints a fresh handle — never a reused or cached one.");
            Assert.That(exports, Does.Contain("try { st_ = _thunk("),
                "the thunk call must sit in a try…");
            Assert.That(exports, Does.Contain("if (args_[0] != 0) Table.Release(args_[0]);"),
                "…whose finally gives the reference back. A release placed after the call is "
                + "skipped whenever the callback body throws.");
        });
    }

    /// <summary>
    /// A handle RESULT is read out and then released — the adapter addref'd it, and once
    /// <c>Table.TryGet</c> has handed back the object the managed reference roots it, so the
    /// table slot is pure overhead. Keeping it leaks one slot per invocation.
    /// </summary>
    [Test]
    public void AHandleResult_IsReadOut_AndItsTableSlotGivenBack()
    {
        var exports = Exports(HandleReturnSurface());

        Assert.Multiple(() =>
        {
            Assert.That(exports, Does.Contain("var stv_ = Table.TryGet(r_, out rv_);"));
            Assert.That(exports, Does.Contain("Table.Release(r_);"),
                "the adapter's addref must be balanced here, or every invocation leaks a slot.");
            Assert.That(exports, Does.Contain("callback result handle was stale"),
                "a stale result handle is a loud failure, not a silent null.");
        });
    }

    /// <summary>
    /// A String result is freed through the allocator that produced it. The adapter allocates
    /// via <c>g_shim.alloc</c> — which IS this side's <c>NativeMemory</c>, reached back across
    /// the boundary — so freeing here is the matching half, not a cross-allocator free.
    /// </summary>
    [Test]
    public void AStringResult_IsFreedByTheAllocatorThatProducedIt()
    {
        var exports = Exports(StringReturnSurface());

        Assert.Multiple(() =>
        {
            Assert.That(exports, Does.Contain("var rs_ = Utf8ToString((byte*)r_);"));
            Assert.That(exports, Does.Contain("NativeMemory.Free((void*)r_);"),
                "g_shim.alloc is NativeMemory.Alloc reached from native code, so NativeMemory "
                + "is the correct free — see blnet_alloc in the shim's core exports.");
        });
    }

    // ====================================================================================
    // 4. Does the generated C# actually COMPILE? Roslyn, not eyeballing.
    // ====================================================================================

    /// <summary>
    /// Every new shape through the real C# compiler. The emitted-text assertions above prove
    /// the SHAPE; only a compile proves the text is valid C# — that <c>(void*)args_[0]</c> is
    /// legal in this context, that the cast on a handle result binds, that <c>NativeMemory</c>
    /// is in scope in the generated file.
    ///
    /// <para>Without this, a typo in a string literal in the emitter ships as a build failure in
    /// <c>obj/gen/shim</c> that the user never wrote and cannot read.</para>
    /// </summary>
    [TestCase("handle parameter")]
    [TestCase("handle return")]
    [TestCase("String parameter")]
    [TestCase("String return")]
    public void EachNewSlotShape_ProducesCompilableCSharp(string shape)
    {
        var surface = shape switch
        {
            "handle parameter" => HandleParamSurface(),
            "handle return" => HandleReturnSurface(),
            "String parameter" => StringParamSurface(),
            _ => StringReturnSurface(),
        };

        NetStubHarness.AssertShimCompiles(
            new[] { _probe.Path },
            Exports(surface),
            $"§8.4's {shape} slot must generate C# that compiles.");
    }

    // ====================================================================================
    // 4b. The NATIVE adapter — the other half of the same ownership rules.
    // ====================================================================================

    /// <summary>
    /// A String-slotted callback describes a STRING slot and copies the buffer.
    ///
    /// <para>The descriptor is what makes the QUEUED path deep-copy: the pointer the managed
    /// side passes is freed as soon as the thunk returns, so a slot labelled VALUE would leave
    /// the pump reading freed memory. The <c>std::string</c> copy in the adapter is the second
    /// half — the BasicLang lambda may outlive even the deep copy.</para>
    /// </summary>
    [Test]
    public void AStringSlottedCallback_DescribesAStringSlot_AndCopiesTheBuffer()
    {
        var cpp = LowerProbeProgram("Slots.StringIn(Sub(s As String) Console.WriteLine(s))");

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("{BLNET_SLOT_STRING, 0}"),
                "a VALUE label here leaves the pump reading a buffer the managed side already "
                + "freed — and nothing on that path is a compile error.");
            Assert.That(cpp, Does.Contain("std::string(reinterpret_cast<const char*>(blnet_a[0])"),
                "the adapter takes a COPY; the borrowed buffer dies when the thunk returns.");
        });
    }

    /// <summary>
    /// The adapter allocates a String result through <c>g_shim.alloc</c> — the MANAGED
    /// allocator — so the dispatcher's <c>NativeMemory.Free</c> is the matching half. Packing a
    /// pointer into the <c>std::string</c> would hand back memory that dies with the lambda
    /// frame; allocating with <c>malloc</c> would make the managed free a cross-allocator free.
    /// </summary>
    [Test]
    public void TheNativeAdapter_AllocatesAStringResultFromTheManagedAllocator()
    {
        var cpp = LowerProbeProgram("Slots.StringOut(Function() \"HELLO\")");

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("BasicLang::blnet::g_shim.alloc("),
                "the buffer must come from the allocator the managed side frees with.");
            Assert.That(cpp, Does.Contain("blnet_r.size()"),
                "…sized from the std::string, with room for the NUL.");
        });
    }

    /// <summary>
    /// <b><c>NetRef::Share</c> itself, run.</b> The whole handle contract rests on this one
    /// primitive: it must take a SECOND reference, so the caller's release and the callback's
    /// release are independent.
    ///
    /// <para>The adapter that calls it is unreachable from BasicLang today (see the next test),
    /// so this proves the primitive directly rather than through generated code. The transcript
    /// is the point: one <c>ADDREF</c> when the second reference is taken, then two
    /// <c>REL</c>s — one per reference. <c>NetRef(word)</c> in its place would print no
    /// <c>ADDREF</c> and still two <c>REL</c>s, which is the double release the contract
    /// forbids; a non-owning view would print one <c>REL</c> and leave the callback's copy
    /// dangling.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void NetRefShare_TakesASecondIndependentReference()
    {
        var surface = new NetSurface(
            new[]
            {
                new NetMemberDescriptor(
                    "Ping", "Contoso.Widget", NetMemberCategory.Method, isStatic: true, arity: 0,
                    "System.Void", Array.Empty<NetParameterDescriptor>()),
            },
            Array.Empty<string>());

        var stub = NetStubHarness.StubTranslationUnit(
            new[]
            {
                new NetStubHarness.StubSlot(
                    NetNameMangler.Mangle(surface.Members[0]), "[]() -> int32_t { return 0; }"),
            },
            shimSetup:
            "        BasicLang::blnet::g_shim.addref = [](uint64_t h) -> int32_t {"
            + " std::printf(\"ADDREF:%llu\\n\", (unsigned long long)h); return 0; };\n"
            + "        BasicLang::blnet::g_shim.release = [](uint64_t h) -> int32_t {"
            + " std::printf(\"REL:%llu\\n\", (unsigned long long)h); return 0; };");

        var main = """
            #include "blnet_proxies.g.hpp"
            #include <cstdio>

            int main() {
                BasicLang::blnet::NetRef caller(99);          // what the dispatcher minted
                {
                    // Exactly what the adapter does with an incoming handle word.
                    BasicLang::blnet::NetRef inner =
                        BasicLang::blnet::NetRef::Share(caller.get());
                    std::printf("INNER:%llu\n", (unsigned long long)inner.get());
                }                                            // inner's reference goes back
                std::printf("AFTER-INNER\n");
                return 0;                                    // caller's reference goes back
            }
            """;

        var output = NetStubHarness.RunWithStub(main, surface, stub).Replace("\r\n", "\n");

        Assert.That(output, Is.EqualTo(
            "ADDREF:99\nINNER:99\nREL:99\nAFTER-INNER\nREL:99\n"),
            "one ADDREF for the second reference, then one REL per reference. No ADDREF with "
            + "two RELs is the double release; one REL total means the callback's copy was "
            + "never really its own.");
    }

    /// <summary>
    /// <b>Why the handle half of the ADAPTER has no run test, pinned so the gap cannot rot.</b>
    ///
    /// <para>§8.4's handle contract is live on the MANAGED side — the dispatcher generates and
    /// compiles, which is what unblocks <c>&lt;NetProxy&gt;</c> declared surfaces and what the
    /// <c>BL6006</c> on a 32-type project actually was. The native adapter's half is emitted
    /// too, but it can only be REACHED by a BasicLang lambda that takes a .NET object — and the
    /// C++ backend has no mapping for a .NET reference type in a lambda parameter, for
    /// <c>Match</c> exactly as for anything else.</para>
    ///
    /// <para>That is a <c>CppCapabilityChecker</c> limitation, not a §8.4 one, and this test
    /// says so by asserting the refusal names the LAMBDA PARAMETER. When that mapping lands,
    /// this test goes red — which is the signal to write the handle adapter's run proof, the one
    /// this fixture cannot write today.</para>
    /// </summary>
    [Test]
    public void AHandleSlottedCallback_IsUnreachableFromBasicLang_ForAReasonThatIsNot84()
    {
        var ex = Assert.Throws<BasicLang.Compiler.CodeGen.CPlusPlus.CppCapabilityException>(
            () => NetStubHarness.CompileWithSurface("""
                Module M
                 Sub Main()
                  Dim r As New Regex("a")
                  Console.WriteLine(r.Replace("abc", Function(m As Match) "X"))
                 End Sub
                End Module
                """, optimize: false));

        Assert.That(ex!.Message, Does.Contain("parameter 'm' of"),
            "the blocker must still be the lambda PARAMETER's type mapping. If this stops being "
            + "the refusal, a BasicLang lambda can now take a .NET object — and §8.4's handle "
            + "adapter needs the run proof this fixture had to leave out: " + ex.Message);
    }

    /// <summary>
    /// <b>The run proof.</b> A BasicLang lambda returning a String, invoked through the real
    /// thunk, with the word making the whole round trip: the adapter allocates through
    /// <c>g_shim.alloc</c>, the stub reads the buffer back as text and frees it.
    ///
    /// <para>The stub plays the managed side here (the stub runtime REPLACES the shim), so what
    /// this proves is the NATIVE half of the String-return contract: that a
    /// <c>std::string</c> returned by a BasicLang lambda arrives as a readable NUL-terminated
    /// buffer from the allocator the managed side would free with.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AStringReturningCallback_RoundTripsThroughTheRealThunk()
    {
        var (cpp, surface) = NetStubHarness.CompileWithSurface("""
            Using Slot.Probe

            Module M
             Sub Main()
              Slots.StringOut(Function() "HELLO")
             End Sub
            End Module
            """, optimize: false, resolver: _resolver);

        var slot = NetNameMangler.Mangle(surface.Members.Single(m => m.Name == "StringOut"));

        var stub = NetStubHarness.StubTranslationUnit(
            new[]
            {
                // Plays the managed side: invoke the callback through the real thunk, then read
                // and free exactly as the generated dispatcher's String arm does.
                new NetStubHarness.StubSlot(slot,
                    "[](uint64_t cb, char** result) -> int32_t {"
                    + " uint64_t r = 0;"
                    + " BasicLang::blnet::blnet_invoke_callback(cb, nullptr, 0, &r);"
                    + " std::printf(\"CB:%s\\n\", r ? reinterpret_cast<const char*>(r) : \"<null>\");"
                    + " if (r && BasicLang::blnet::g_shim.free_) BasicLang::blnet::g_shim.free_("
                    + "reinterpret_cast<void*>(r));"
                    + " *result = stub_strdup(\"done\"); return 0; }"),
            },
            shimSetup:
            "        BasicLang::blnet::g_shim.alloc = [](int64_t n) -> void* {"
            + " return std::malloc(static_cast<size_t>(n)); };\n"
            + "        BasicLang::blnet::g_shim.free_ = [](void* p) { std::free(p); };");

        var output = NetStubHarness.RunWithStub(cpp, surface, stub).Replace("\r\n", "\n");

        Assert.That(output, Does.Contain("CB:HELLO"),
            "the lambda's String must reach the caller as a readable buffer. '<null>' means the "
            + "adapter never allocated; garbage means it packed a pointer to a std::string that "
            + "had already died with the lambda frame.");
    }

    // ====================================================================================
    // 5. The refusals still refuse, at the gate that produces the build error.
    // ====================================================================================

    [Test]
    public void AnObjectSlottedDelegate_StillFailsTheShim_Loudly()
    {
        var surface = DelegateSurface(
            "System.Threading.ParameterizedThreadStart", "System.Void(System.Object)");

        var ex = Assert.Throws<NotSupportedException>(() => Exports(surface));

        Assert.That(ex!.Message, Does.Contain("System.Object"),
            "the offender must be named — this is a build error in code the user never wrote, "
            + "so the message is the only thing they have.");
    }

    // ====================================================================================
    // Fixture helpers.
    // ====================================================================================

    /// <summary>
    /// A probe assembly, because the four shapes have to hang off a method that REALLY EXISTS:
    /// the generated export body calls it, so a made-up declaring type is CS0400 in the shim
    /// rather than a proof of anything. <c>Box</c> is deliberately an ordinary reference type
    /// with no marshal row — the "opaque handle" default §8.3 relies on.
    /// </summary>
    private const string ProbeSource = """
        namespace Slot.Probe
        {
            public sealed class Box { public int V; }

            public static class Slots
            {
                public static void HandleIn(System.Action<Box> f) => f(new Box());
                public static Box HandleOut(System.Func<Box> f) => f();
                public static void StringIn(System.Action<string> f) => f("x");
                public static string StringOut(System.Func<string> f) => f();
            }
        }
        """;

    private static NetStubHarness.ProbeAssembly _probe;
    private static NetTypeResolver _resolver;

    [OneTimeSetUp]
    public void BuildProbe()
    {
        _probe = new NetStubHarness.ProbeAssembly("SlotProbe", ProbeSource);
        _resolver = NetTypeResolver.Create(
            NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { _probe.Path }));
    }

    [OneTimeTearDown]
    public void DropProbe() => _probe?.Dispose();

    /// <summary>Lowers a one-statement probe program and returns the generated C++.</summary>
    private static string LowerProbeProgram(string statement) =>
        NetStubHarness.CompileWithSurface(
            "Using Slot.Probe\n\nModule M\n Sub Main()\n  " + statement + "\n End Sub\nEnd Module\n",
            optimize: false, resolver: _resolver).Cpp;

    private static string Exports(NetSurface surface) =>
        NetShimGenerator.Emit(surface, "Shim")[NetShimGenerator.ExportsFileName];

    /// <summary>The probe-backed surface for one shape, matching <see cref="ProbeSource"/>.</summary>
    private static NetSurface ProbeSurface(
        string member, string delegateFullName, string invokeSignature, string returnType) =>
        new(
            new[]
            {
                new NetMemberDescriptor(
                    member, "Slot.Probe.Slots", NetMemberCategory.Method, isStatic: true,
                    arity: 0, returnType,
                    new[]
                    {
                        new NetParameterDescriptor(
                            NetRefKind.None, delegateFullName, invokeSignature),
                    }),
            },
            Array.Empty<string>());

    private static NetSurface HandleParamSurface() => ProbeSurface(
        "HandleIn", "System.Action<Slot.Probe.Box>", "System.Void(Slot.Probe.Box)",
        "System.Void");

    private static NetSurface HandleReturnSurface() => ProbeSurface(
        "HandleOut", "System.Func<Slot.Probe.Box>", "Slot.Probe.Box()", "Slot.Probe.Box");

    private static NetSurface StringParamSurface() => ProbeSurface(
        "StringIn", "System.Action<System.String>", "System.Void(System.String)", "System.Void");

    private static NetSurface StringReturnSurface() => ProbeSurface(
        "StringOut", "System.Func<System.String>", "System.String()", "System.String");

    private static NetSurface DelegateSurface(string delegateFullName, string invokeSignature) =>
        new(
            new[]
            {
                new NetMemberDescriptor(
                    "Take", "Contoso.Widget", NetMemberCategory.Method, isStatic: true, arity: 0,
                    "System.Void",
                    new[]
                    {
                        new NetParameterDescriptor(
                            NetRefKind.None, delegateFullName, invokeSignature),
                    }),
            },
            Array.Empty<string>());
}

/// <summary>Slot-kind names as strings — <c>NetSlotKind</c> is internal, NUnit needs public.</summary>
internal static class NetSlotKindName
{
    internal const string Value = "Value";
    internal const string String = "String";
    internal const string Handle = "Handle";
}

/// <summary>Reaches the internal classifier and reports its verdict as a plain string.</summary>
internal static class NetDelegateDispatchAccess
{
    internal static bool TryClassify(string typeFullName, out string kind, out string refusal)
    {
        var ok = NetDelegateDispatch.TryClassifySlot(typeFullName, out var k, out refusal);
        kind = ok ? k.ToString() : null;
        return ok;
    }
}
