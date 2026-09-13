using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.CodeGen.Net;
using BasicLang.Compiler.ProjectSystem;
using BasicLang.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// <c>blnet_facade.g.hpp</c> — the ergonomic C++ rendering of the proxy slots
/// (plan <c>2026-09-13-blnet-cpp-facade.md</c>, Tasks 1-3: methods, constructors and properties).
///
/// <para><b>⛔ The trap this fixture is built to avoid.</b> "The facade compiles" is NOT
/// coverage — an EMPTY facade compiles perfectly, and so does one that silently dropped half the
/// surface. The load-bearing assertion here is therefore a SET IDENTITY
/// (<see cref="EverySlotIsEitherRenderedOrSkippedWithAReason"/>): every slot is either rendered
/// or on a skip list that states why. Compilation is the SECOND oracle, for shape.</para>
///
/// <para>That split matters because the two failures look nothing alike. A slot that stops
/// rendering is invisible to a compile test — the header just gets smaller. A slot that renders
/// WRONG is invisible to the coverage test — the set is still complete.</para>
/// </summary>
[TestFixture]
public class NetFacadeEmitterTests
{
    private const string ProbeSource = """
        namespace Fac.Probe
        {
            public sealed class Widget { }
            public sealed class Gadget { }

            // Task 2: a type with INSTANCE members. It therefore gets a handle and a wrapper,
            // and becomes nameable in other signatures under D7.
            public sealed class Counter
            {
                private int _n;

                // Task 3 / D5: real constructors. Two of them, so the overload set is not trivial,
                // and neither may collide with the handle-ADOPTING constructor.
                public Counter() { }
                public Counter(int start) { _n = start; }

                public int Bump(int by) { _n += by; return _n; }
                public string Label() => "n=" + _n;

                // Task 3 / D6: a property renders as get_Value(). Its SETTER is a different
                // question — see the emitter's remarks: a declared surface draws read slots only.
                public int Value => _n;
            }

            public static class Api
            {
                // Renders: static, single scalar/string wire forms, scalar return.
                public static int    Twice(int v) => v + v;
                public static string Shout(string s) => s + "!";
                public static void   Ping() { }

                // D7 in BOTH directions. Counter has instance members, so it has a wrapper and
                // these render as the wrapper type rather than as a bare NetRef.
                public static Counter Make() => new Counter();
                public static int     Read(Counter c) => c.Bump(0);

                // Does NOT render in v1 — each exercises one ClassifyForFacade arm.
                public static int    ByRefArg(ref int v) => v;

                // D8: BOTH omitted. Widget and Gadget are distinct .NET types with NO members, so
                // neither has a wrapper and both still collapse onto NetRef — one C++ signature.
                // (Had they been wrapper types, D7 would have made these distinguishable and both
                // would render; that asymmetry is the point of keying the collision check on the
                // facade types rather than on the wire form.)
                public static void   Ambiguous(Widget w) { }
                public static void   Ambiguous(Gadget g) { }
            }
        }
        """;

    private static ProbeAssembly _probe;
    private static NetSurface _surface;

    [OneTimeSetUp]
    public void Build()
    {
        _probe = new ProbeAssembly("FacProbe", ProbeSource);
        var project = new ProjectFile();
        project.NetProxyTypes.Add("Fac.Probe.Api");
        project.NetProxyTypes.Add("Fac.Probe.Counter");

        var resolver = NetTypeResolver.Create(
            NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { _probe.Path }));

        _surface = NetSurfaceCollector.Collect(
            Array.Empty<BasicLang.Compiler.IR.IRModule>(), project, () => resolver,
            new List<NetReferenceDiagnostic>());

        Assert.That(_surface.IsNonEmpty, Is.True, "the probe must draw a surface");
    }

    [OneTimeTearDown]
    public void Drop() => _probe?.Dispose();

    private static string Facade() =>
        NetProxyEmitter.Emit(_surface, "FacProbe.Blnet.dll")[NetProxyEmitter.FacadeFileName];

    // ------------------------------------------------------------------------------------

    /// <summary>
    /// <b>THE invariant.</b> Rendered ∪ skipped must equal the whole slot set, as a SET — a count
    /// would pass while one slot silently swapped for another.
    ///
    /// <para>Derived from <c>Plan(surface)</c>, the same list the proxy emitter renders, so a
    /// slot that appears in the proxy table and nowhere in the facade's two buckets fails here.
    /// This is §7.2's rule turned on the facade itself: an omission nobody is told about leaves
    /// the surface quietly meaning less than it says.</para>
    /// </summary>
    [Test]
    public void EverySlotIsEitherRenderedOrSkippedWithAReason()
    {
        var rendered = NetProxyEmitter.FacadeRendered(_surface);
        var skipped = NetProxyEmitter.FacadeSkips(_surface);
        var all = _surface.Members.Select(NetNameMangler.Mangle).ToList();

        Assert.That(all, Is.Not.Empty, "guard: no slots, so this proves nothing");

        Assert.That(rendered.Concat(skipped.Select(s => s.SlotName)),
            Is.EquivalentTo(all),
            "a proxy slot is neither rendered in the facade nor on its skip list. Both buckets "
            + "come from the SAME Plan(surface) the proxy table is built from, so a slot missing "
            + "from both means the facade silently covers less than the surface. Add a render or "
            + "a ClassifyForFacade arm STATING THE REASON — never let one fall through.");

        Assert.That(skipped.Select(s => s.Reason), Has.All.Not.Empty,
            "a skip with no reason is exactly the silent omission this test exists to prevent.");
    }

    /// <summary>The three shapes v1 promises to render actually render.</summary>
    [Test]
    public void StaticMethodsWithSimpleSignaturesAreRendered()
    {
        var text = Facade();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("static int32_t Twice(int32_t a0)"),
                "a scalar-in/scalar-out static method must render");
            Assert.That(text, Does.Contain("Shout(const char* a0)"),
                "a String parameter renders as the proxy's own const char* spelling");
            Assert.That(text, Does.Contain("static void Ping()"),
                "a void, no-argument static method must render");
            Assert.That(text, Does.Contain("struct Api {"),
                "the declaring type becomes a struct (decision D2)");
        });
    }

    /// <summary>
    /// Decision D8: two slots sharing one C++ signature are BOTH omitted, never resolved by
    /// picking one.
    ///
    /// <para>§8.3 collapses every handle-represented type onto <c>NetRef</c>, so
    /// <c>Ambiguous(Widget)</c> and <c>Ambiguous(Gadget)</c> are one C++ signature. Rendering
    /// either would make <c>Ambiguous(someGadget)</c> silently call the <c>Widget</c> overload —
    /// a WRONG answer, which is worse than a missing one. The mangled slots remain callable.</para>
    /// </summary>
    [Test]
    public void TwoSlotsSharingOneCppSignatureAreBothOmitted()
    {
        var text = Facade();

        Assert.That(text, Does.Not.Contain("Ambiguous("),
            "a colliding overload was rendered. With both Widget and Gadget arriving as NetRef "
            + "these are ONE C++ signature, so any rendering silently binds one .NET member to "
            + "calls meant for the other. Omit both and name them in the OMITTED comment.");

        Assert.That(text, Does.Contain("OMITTED"),
            "the collision must be REPORTED in the header, not silently dropped — the reader "
            + "needs to know to reach for the mangled slot.");
    }

    /// <summary>
    /// Decision D1: never a bare <c>namespace System</c> at global scope.
    ///
    /// <para>This header is generated into a project the user did not write and cannot edit. A
    /// global <c>System</c> would collide with any user namespace or type of that name and turn
    /// a program that compiled into one that does not — the one thing a generated header must
    /// never do. Everything sits under <c>BasicLang::netfx</c>; <c>using namespace</c> is opt-in.
    /// </para>
    /// </summary>
    [Test]
    public void NothingIsEmittedAtGlobalScope()
    {
        var text = Facade();

        const string rootOpen = "namespace BasicLang { namespace netfx {";
        const string rootClose = "}} /* namespace BasicLang::netfx */";

        var openAt = text.IndexOf(rootOpen, StringComparison.Ordinal);
        var closeAt = text.IndexOf(rootClose, StringComparison.Ordinal);

        Assert.That(openAt, Is.GreaterThanOrEqualTo(0), "the facade must open the netfx root (D1)");
        Assert.That(closeAt, Is.GreaterThan(openAt), "…and close it after opening it");

        // The real property, asserted STRUCTURALLY rather than by indentation: every other
        // namespace opener lies strictly INSIDE the root's span. An earlier draft of this test
        // looked for `namespace` at column 0 and failed on correct output, because the emitter
        // does not indent nested namespaces — the heuristic, not the emitter, was wrong.
        var strays = new List<string>();
        var scan = 0;
        while (true)
        {
            var at = text.IndexOf("\nnamespace ", scan, StringComparison.Ordinal);
            if (at < 0) break;
            scan = at + 1;
            if (at > openAt && at < closeAt) continue;      // inside the root — fine
            if (text.IndexOf(rootOpen, at, StringComparison.Ordinal) == at + 1) continue;
            strays.Add(text.Substring(at + 1, Math.Min(40, text.Length - at - 1)).Split('\n')[0]);
        }

        Assert.That(strays, Is.Empty,
            "a namespace is opened OUTSIDE the BasicLang::netfx root. A generated header must "
            + "never put `namespace System` at global scope: it would collide with any user "
            + "namespace or type of that name, in a file the user did not write and cannot edit, "
            + "turning a program that compiled into one that does not. Got: "
            + string.Join(" | ", strays));
    }

    /// <summary>
    /// <b>The second oracle: the emitted header must COMPILE, and must forward to the right
    /// slot.</b> The text assertions above can all pass on a header no compiler accepts —
    /// a malformed signature, a missing include, a namespace that never closes — because they
    /// only ever look at substrings.
    ///
    /// <para>This deliberately goes further than compiling. It RUNS, against
    /// <see cref="NetStubHarness"/>'s stub table, with each stub returning a value the caller
    /// could not have produced by accident: <c>Twice</c> is stubbed to multiply by TEN, so a
    /// facade that dropped its argument, passed a default, or forwarded to the wrong slot
    /// yields something other than 210. An identity stub would have proved only that a number
    /// survived the trip — the same mistake that made this repo's first delegate-wire test pass
    /// on broken rows.</para>
    ///
    /// <para>The real <c>blnet_startup.g.cpp</c> is excluded by the harness (the stub defines
    /// <c>g_net</c> instead), so no shim, no .NET runtime and no <c>win-x64</c> publish is
    /// involved — this runs on Linux as readily as on Windows.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void TheEmittedFacadeCompilesAndForwardsToTheRightSlot()
    {
        var stub = NetStubHarness.StubTranslationUnit(new[]
        {
            // x10, NOT identity: a facade that drops or reorders arguments must not match.
            new NetStubHarness.StubSlot(Slot("Twice"),
                "[](int32_t a0, int32_t* result) -> int32_t { *result = a0 * 10; return 0; }"),
            new NetStubHarness.StubSlot(Slot("Shout"),
                "[](const char* a0, char** result) -> int32_t {"
                + " std::printf(\"ARG:%s\\n\", a0);"
                + " *result = stub_strdup(\"SHOUTED\"); return 0; }"),
            new NetStubHarness.StubSlot(Slot("Ping"),
                "[]() -> int32_t { std::printf(\"PING\\n\"); return 0; }"),

            // Task 2. Make hands back a handle the wrapper must adopt; Bump's result depends on
            // BOTH the receiver and the argument, so dropping the receiver or shifting the
            // arguments left by one changes the number; Read proves a wrapper parameter is
            // unwrapped to that same handle.
            new NetStubHarness.StubSlot(Slot("Make"),
                "[](uint64_t* result) -> int32_t { *result = 42; return 0; }"),
            new NetStubHarness.StubSlot(Slot("Bump"),
                "[](uint64_t self, int32_t a0, int32_t* result) -> int32_t {"
                + " std::printf(\"SELF:%llu\\n\", (unsigned long long)self);"
                + " *result = (int32_t)self + a0; return 0; }"),
            new NetStubHarness.StubSlot(Slot("Read"),
                "[](uint64_t a0, int32_t* result) -> int32_t {"
                + " *result = (int32_t)a0 * 100; return 0; }"),

            // Task 3. The constructor slot MAKES the object and hands back its handle, so a
            // wrapper that forgot to adopt the result reads back 0. get_Value derives from the
            // receiver, so a getter that lost it reads 0 too.
            new NetStubHarness.StubSlot(SlotCtor(1),
                "[](int32_t a0, uint64_t* result) -> int32_t {"
                + " *result = (uint64_t)(a0 * 3); return 0; }"),
            new NetStubHarness.StubSlot(Slot("Value"),
                "[](uint64_t self, int32_t* result) -> int32_t {"
                + " *result = (int32_t)self * 2; return 0; }"),
        });

        var main = """
            #include "blnet_facade.g.hpp"
            #include <cstdio>

            int main() {
                using namespace BasicLang::netfx;
                std::printf("%d\n", Fac::Probe::Api::Twice(21));
                std::printf("%s\n", Fac::Probe::Api::Shout("hi").c_str());
                Fac::Probe::Api::Ping();

                /* D7: a handle return arrives as the wrapper, not as a NetRef. */
                Fac::Probe::Counter c = Fac::Probe::Api::Make();
                std::printf("BUMP:%d\n", c.Bump(5));
                std::printf("READ:%d\n", Fac::Probe::Api::Read(c));
                std::printf("RAW:%llu\n", (unsigned long long)c.raw().get());

                /* D5: a real constructor CREATES the object — not a factory call. */
                Fac::Probe::Counter made(7);
                std::printf("CTOR:%llu\n", (unsigned long long)made.raw().get());
                /* D6: a property is a plain get_X(). */
                std::printf("VAL:%d\n", made.get_Value());
                return 0;
            }
            """;

        var output = NetStubHarness.RunWithStub(main, _surface, stub).Replace("\r\n", "\n");

        Assert.That(output,
            Is.EqualTo("210\nARG:hi\nSHOUTED\nPING\nSELF:42\nBUMP:47\nREAD:4200\nRAW:42\n"
                       + "CTOR:21\nVAL:42\n"),
            "the facade compiled but did not forward correctly.\n"
            + "  '0' or a default        -> the argument never reached the slot;\n"
            + "  missing ARG line        -> the string parameter was dropped;\n"
            + "  SELF:0                  -> the receiver was not forwarded from the handle;\n"
            + "  BUMP:5 (not 47)         -> the receiver was dropped and the arguments shifted;\n"
            + "  READ:0                  -> a wrapper parameter was not unwrapped to its handle;\n"
            + "  RAW:0                   -> the wrapper did not adopt the returned handle;\n"
            + "  CTOR:0                  -> a real constructor ran the slot and discarded its\n"
            + "                             handle, leaving the wrapper empty;\n"
            + "  VAL:0                   -> a property getter lost the receiver.");
    }

    // ---- Task 2: instance members, the handle, and D7 ------------------------------------

    /// <summary>
    /// D4: a type with instance members holds its <c>NetRef</c> PRIVATELY and exposes
    /// <c>raw()</c>; a static-only type gets neither.
    ///
    /// <para>The negative half is the load-bearing one. If every type got a handle,
    /// <c>Console</c> would become a constructible value with a meaningless identity, and D7
    /// would start offering it as a parameter type.</para>
    /// </summary>
    [Test]
    public void OnlyTypesWithInstanceMembersCarryAHandle()
    {
        var text = Facade();

        Assert.Multiple(() =>
        {
            Assert.That(TypeBlock(text, "Counter"),
                Does.Match(@"\nprivate:\n\s*BasicLang::blnet::NetRef blnet_handle_;"),
                "Counter has instance members, so it must hold its handle — and hold it PRIVATELY "
                + "(D4), so the wrapper cannot be rebound to a different object behind its back.");

            Assert.That(text, Does.Contain("const BasicLang::blnet::NetRef& raw() const;"),
                "the handle must be reachable for the mangled slots and for proxies the facade "
                + "does not render — by const reference, so the common use costs no refcount traffic.");

            Assert.That(TypeBlock(text, "Api"), Does.Not.Contain("NetRef blnet_handle_"),
                "Api is static-only, so it must NOT carry a handle: a Console-shaped type with a "
                + "handle is a constructible value with no meaning, and D7 would offer it as a "
                + "parameter type.");
        });
    }

    /// <summary>
    /// D3: the receiver stops being an explicit first argument — that is the whole ergonomic win.
    /// The proxy still takes it first (<c>EmitProxyBody</c> prepends <c>const NetRef&amp; self</c>),
    /// so the facade must supply it from its own field.
    /// </summary>
    [Test]
    public void InstanceMethodsDropTheReceiverAndSupplyItFromTheHandle()
    {
        var text = Facade();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("int32_t Bump(int32_t a0) const;"),
                "an instance method renders as an ordinary member function with the receiver GONE "
                + "from its parameter list. It must be const: D7 passes wrappers as const&, so a "
                + "non-const member function could not be called on one.");

            Assert.That(text, Does.Contain("BasicLang::net::" + Slot("Bump") + "(blnet_handle_, a0)"),
                "the receiver must be forwarded as the proxy's FIRST argument, from the wrapper's "
                + "own handle. Dropping it shifts every argument left by one.");

            Assert.That(text, Does.Not.Contain("static int32_t Bump"),
                "an instance method must not render as static.");
        });
    }

    /// <summary>
    /// D7: a handle-typed parameter or return is the WRAPPER type when that type has one, in both
    /// directions — and the argument is unwrapped to the raw handle at the call.
    ///
    /// <para>Without this the signature is <c>NetRef</c> everywhere and the facade buys nothing
    /// over the mangled slot for anything but scalars.</para>
    /// </summary>
    [Test]
    public void HandleTypesInTheSurfaceRenderAsTheirWrapper()
    {
        var text = Facade();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("static ::BasicLang::netfx::Fac::Probe::Counter Make();"),
                "a handle RETURN whose type has a wrapper must render as that wrapper, not NetRef.");

            Assert.That(text,
                Does.Contain("static int32_t Read(const ::BasicLang::netfx::Fac::Probe::Counter& a0);"),
                "a handle PARAMETER whose type has a wrapper must render as that wrapper, taken by "
                + "const reference.");

            Assert.That(text, Does.Contain("(a0.raw())"),
                "a wrapper argument must be unwrapped to its handle at the call — the proxy takes "
                + "a NetRef, not a facade type.");

            Assert.That(text,
                Does.Contain("::BasicLang::netfx::Fac::Probe::Counter(::BasicLang::netfx::adopt_handle, "
                             + "BasicLang::net::" + Slot("Make")),
                "a wrapper RESULT must ADOPT the proxy's returned handle — through the tagged "
                + "constructor, since the untagged spelling now belongs to D5's real constructors.");
        });
    }

    /// <summary>
    /// The ordering property the three-phase emission exists for: EVERY type is forward-declared
    /// before ANY member body is defined.
    ///
    /// <para>This is not stylistic. <c>Fac.Probe.Api</c> sorts before <c>Fac.Probe.Counter</c> and
    /// returns one, so a body emitted inside the struct — which is what Task 1 did while every
    /// signature was a scalar — names an incomplete type and does not compile. A compile test
    /// catches that only while the probe happens to be ordered badly; this asserts the invariant
    /// directly.</para>
    /// </summary>
    [Test]
    public void EveryTypeIsForwardDeclaredBeforeAnyBodyIsDefined()
    {
        var text = Facade();

        var lastForward = text.LastIndexOf("struct Counter;", StringComparison.Ordinal);
        var firstBody = FirstMemberDefinition(text);

        Assert.Multiple(() =>
        {
            Assert.That(lastForward, Is.GreaterThanOrEqualTo(0),
                "every facade type must be forward-declared (D7 lets one name another).");
            Assert.That(firstBody, Is.GreaterThan(lastForward),
                "a member body is defined before the forward declarations are complete. A body "
                + "needs its parameter and return types COMPLETE, so all bodies must follow all "
                + "declarations.");

            // Scoped to the struct's own text. An unbounded regex from "struct Api {" would
            // happily run past the closing brace and match a body in the out-of-line section,
            // which is precisely where bodies belong — it would fail on correct output.
            Assert.That(TypeBlock(text, "Api"), Does.Not.Contain("return BasicLang::net::"),
                "a member body was emitted INSIDE the struct. That compiles only while no "
                + "signature names a sibling facade type — exactly the case D7 introduces.");
        });
    }

    /// <summary>
    /// Offset of the first OUT-OF-LINE MEMBER DEFINITION — an <c>inline</c> line that qualifies a
    /// name with <c>Type::</c> and opens a parameter list.
    ///
    /// <para>Not simply the first <c>inline</c>: the file also emits
    /// <c>inline constexpr adopt_handle_t adopt_handle{};</c> ahead of the forward declarations,
    /// which is correct — the tag is a complete type that depends on nothing — but is not a member
    /// body and must not be mistaken for one.</para>
    /// </summary>
    private static int FirstMemberDefinition(string text)
    {
        var scan = 0;
        while (true)
        {
            var at = text.IndexOf("\ninline ", scan, StringComparison.Ordinal);
            if (at < 0) return -1;
            scan = at + 1;

            var lineEnd = text.IndexOf('\n', at + 1);
            var line = lineEnd < 0 ? text.Substring(at + 1) : text.Substring(at + 1, lineEnd - at - 1);
            if (line.Contains("::", StringComparison.Ordinal)
                && line.Contains("(", StringComparison.Ordinal))
                return at;
        }
    }

    /// <summary>The text of one <c>struct X { … };</c> block, for scoped assertions.</summary>
    private static string TypeBlock(string text, string typeName)
    {
        var start = text.IndexOf("struct " + typeName + " {", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"no 'struct {typeName} {{' in the facade");
        var end = text.IndexOf("\n};", start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), $"'struct {typeName}' never closes");
        return text.Substring(start, end - start);
    }

    // ---- Task 3: constructors (D5) and properties (D6) -----------------------------------

    /// <summary>
    /// D5: a .NET constructor becomes a real C++ constructor that CREATES the object —
    /// <c>Counter c(7)</c>, not a factory call.
    /// </summary>
    [Test]
    public void ConstructorsRenderAsRealCppConstructors()
    {
        var text = Facade();
        var counter = TypeBlock(text, "Counter");

        Assert.Multiple(() =>
        {
            Assert.That(counter, Does.Contain("Counter();"),
                "the parameterless .NET constructor must render.");
            Assert.That(counter, Does.Contain("Counter(int32_t a0);"),
                "a constructor with arguments must render with them.");

            // The body initialises the handle FROM the constructor slot: PlanMember gives every
            // .ctor a Handle return precisely because metadata's System.Void would otherwise emit
            // a slot that constructs and discards.
            Assert.That(text,
                Does.Contain(": blnet_handle_(BasicLang::net::" + SlotCtor(1) + "(a0)) {}"),
                "a constructor must initialise the wrapper's handle from its own slot's result. "
                + "If it does not, the object is created and thrown away and the wrapper is empty.");
        });
    }

    /// <summary>
    /// The adopting constructor must be TAGGED, so it never joins a real constructor's overload
    /// set.
    ///
    /// <para>Untagged, <c>T(NetRef)</c> is ambiguous with any .NET constructor taking a single
    /// handle-typed argument of a type outside the surface — which renders as
    /// <c>T(const NetRef&amp;)</c>. <c>StreamReader(Stream)</c> is exactly that shape, so this is
    /// reachable rather than theoretical.</para>
    /// </summary>
    [Test]
    public void TheAdoptingConstructorIsTaggedSoItCannotCollideWithARealOne()
    {
        var text = Facade();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("struct adopt_handle_t"),
                "the adopt tag type must be defined.");
            Assert.That(TypeBlock(text, "Counter"),
                Does.Contain("Counter(adopt_handle_t, BasicLang::blnet::NetRef blnet_handle);"),
                "the adopting constructor must take the tag.");
            Assert.That(TypeBlock(text, "Counter"),
                Does.Not.Contain("explicit Counter(BasicLang::blnet::NetRef"),
                "the UNTAGGED adopting constructor must be gone — it is ambiguous with any .NET "
                + "constructor taking one handle-typed argument.");
        });
    }

    /// <summary>
    /// D6: a property becomes <c>get_X()</c> — boring and explicit, never an operator. An
    /// <c>operator=</c> that performs a cross-boundary call looks like assignment and costs a
    /// shim round trip.
    /// </summary>
    [Test]
    public void PropertiesRenderAsGetAccessors()
    {
        var text = Facade();

        Assert.Multiple(() =>
        {
            Assert.That(TypeBlock(text, "Counter"), Does.Contain("int32_t get_Value() const;"),
                "an instance property must render as get_X(), const like any other instance member.");

            Assert.That(text, Does.Contain("BasicLang::net::" + Slot("Value") + "(blnet_handle_)"),
                "the getter must forward to the property's own read slot, with the receiver "
                + "supplied from the handle.");

            Assert.That(TypeBlock(text, "Counter"), Does.Not.Contain("operator="),
                "a property must not become an operator (D6).");
        });
    }

    /// <summary>
    /// Every member CATEGORY now renders, so nothing may be skipped merely for being a
    /// constructor, property or field. What remains skippable is SHAPE — a multi-slot result or
    /// argument, or a ByRef parameter.
    ///
    /// <para>Asserted against the skip REASONS rather than a count, so this keeps meaning what it
    /// says when the surface changes.</para>
    /// </summary>
    [Test]
    public void NoSlotIsSkippedMerelyForItsCategory()
    {
        var skipped = NetProxyEmitter.FacadeSkips(_surface);

        Assert.That(skipped.Select(s => s.Reason), Has.None.Contains("renders methods only"),
            "a category-based skip survived Task 3. Constructors, properties and fields all "
            + "render now; only shape (multi-slot results/arguments, ByRef) may exclude a slot.");

        Assert.That(skipped, Is.Not.Empty,
            "guard: the probe must still skip SOMETHING (ByRefArg), or this proves nothing.");
    }

    /// <summary>
    /// The other half of D6: a <c>set_X</c> slot renders as <c>set_X(value)</c>.
    ///
    /// <para><b>Why this test builds its own surface.</b> No declared surface can reach this path.
    /// A <c>&lt;NetProxy&gt;</c> type draws only property READ slots; a setter descriptor is
    /// synthesized only where a BasicLang program actually WRITES the member, and a real build over
    /// <c>System.Console</c> and <c>Regex</c> produces zero <c>set_</c> slots. Testing it through
    /// the probe assembly is therefore impossible — so the setter is synthesized here with the same
    /// production helper (<c>NetAccessorSynthesis.SetterFor</c>) the compiler uses.</para>
    ///
    /// <para>The setter arrives as an ordinary <c>Method</c> named <c>set_X</c> returning
    /// <c>System.Void</c> with the value as its last parameter, so it needs no special case in the
    /// emitter — which is exactly what this pins. If someone later adds one, this goes red.</para>
    /// </summary>
    [Test]
    public void ASynthesizedSetterRendersAsSetX()
    {
        var property = _surface.Members.Single(
            m => m.Name == "Value" && m.DeclaringTypeFullName == "Fac.Probe.Counter");
        var setter = NetAccessorSynthesis.SetterFor(property);

        var withSetter = new NetSurface(
            _surface.Members.Concat(new[] { setter }).ToList(), Array.Empty<string>());
        var text = NetProxyEmitter.Emit(withSetter, "FacProbe.Blnet.dll")[NetProxyEmitter.FacadeFileName];

        Assert.Multiple(() =>
        {
            // const because it describes the WRAPPER, which is unchanged — the .NET object behind
            // the handle is free to mutate. D7 passes wrappers as const&, so a non-const setter
            // could not be called on one.
            Assert.That(text, Does.Contain("void set_Value(int32_t a0) const;"),
                "a synthesized setter must render as set_X taking the value.");

            Assert.That(text, Does.Contain("BasicLang::net::" + NetNameMangler.Mangle(setter)
                                           + "(blnet_handle_, a0)"),
                "the setter must forward the receiver and the value, in that order.");

            Assert.That(text, Does.Not.Contain("get_set_Value"),
                "a name that already carries an accessor prefix must not be prefixed again.");
        });
    }

    /// <summary>The mangled slot name of the probe Counter constructor with that many parameters.</summary>
    private static string SlotCtor(int paramCount) =>
        _surface.Members
            .Where(m => m.Kind == NetMemberCategory.Constructor
                        && m.DeclaringTypeFullName == "Fac.Probe.Counter"
                        && m.Parameters.Count == paramCount)
            .Select(NetNameMangler.Mangle)
            .Single();

    /// <summary>The mangled slot name for the sole probe member of that name.</summary>
    private static string Slot(string memberName) =>
        _surface.Members.Where(m => m.Name == memberName)
            .Select(NetNameMangler.Mangle)
            .Single();

    /// <summary>A C# probe assembly compiled with Roslyn, owning a temp directory.</summary>
    private sealed class ProbeAssembly : IDisposable
    {
        private readonly string _dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "blnet-fac-" + Guid.NewGuid().ToString("N"));

        internal string Path { get; }

        internal ProbeAssembly(string name, string source)
        {
            System.IO.Directory.CreateDirectory(_dir);
            Path = System.IO.Path.Combine(_dir, name + ".dll");

            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                name,
                new[] { Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source) },
                NetTypeResolverTestRefs.FrameworkPaths.Select(
                    p => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(p)),
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                    Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

            Microsoft.CodeAnalysis.Emit.EmitResult emit;
            using (var stream = System.IO.File.Create(Path))
                emit = compilation.Emit(stream);

            Assert.That(emit.Success, Is.True, "probe assembly failed to build: "
                + string.Join("\n", emit.Diagnostics.Where(
                    d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)));
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(_dir, recursive: true); }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
