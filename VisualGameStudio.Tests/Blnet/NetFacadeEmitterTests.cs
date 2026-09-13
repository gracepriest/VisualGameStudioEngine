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
/// (plan <c>2026-09-13-blnet-cpp-facade.md</c>, Task 1: static methods).
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

            public static class Api
            {
                // Renders: static, single scalar/string wire forms, scalar return.
                public static int    Twice(int v) => v + v;
                public static string Shout(string s) => s + "!";
                public static void   Ping() { }

                // Does NOT render in v1 — each exercises one ClassifyForFacade arm.
                public static int    ByRefArg(ref int v) => v;

                // D8: BOTH omitted. Widget and Gadget are distinct .NET types that share ONE
                // C++ wire form (every handle is NetRef), so these are one C++ signature.
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
        });

        var main = """
            #include "blnet_facade.g.hpp"
            #include <cstdio>

            int main() {
                using namespace BasicLang::netfx;
                std::printf("%d\n", Fac::Probe::Api::Twice(21));
                std::printf("%s\n", Fac::Probe::Api::Shout("hi").c_str());
                Fac::Probe::Api::Ping();
                return 0;
            }
            """;

        var output = NetStubHarness.RunWithStub(main, _surface, stub).Replace("\r\n", "\n");

        Assert.That(output, Is.EqualTo("210\nARG:hi\nSHOUTED\nPING\n"),
            "the facade compiled but did not forward correctly. '0' or a default means the "
            + "argument never reached the slot; a missing ARG line means the string parameter "
            + "was dropped; a wrong order means the facade bound a name to the wrong slot.");
    }

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
