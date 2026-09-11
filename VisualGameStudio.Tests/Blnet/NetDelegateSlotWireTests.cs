using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.CodeGen.Net;
using BasicLang.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// Spec §8.4's delegate wire, asserted as a ROUND TRIP per §8.3 scalar row — the
/// admissibility⇄wire-form tie carried forward from Task 8 Step 2b, narrowed to the half that
/// actually has a defect.
///
/// <para><b>What the chip asked for and why this is the useful shape.</b> Step 2b's ask was
/// "every type <c>FirstUnmarshalable</c> ADMITS gets a real wire form from BOTH emitters", and
/// the plan already notes the trap: because both <c>WireOf</c>s default to Handle, "gets a wire
/// form" is trivially true. The same trap has a sharper form here. §8.4 admits a row if it is a
/// blittable scalar — and then carries EVERY admitted row through the same
/// <c>static_cast</c>/<c>unchecked</c> conversion, which is correct for integers and lossy for
/// floating point. Admissibility and wire-form are tied by nothing but a comment. So the
/// invariant worth holding is not "the row has a wire form" but <b>"the row survives the wire"</b>.
///
/// <para><b>The contract.</b> The delegate wire carries each argument as a 64-bit WORD. A word
/// is a BIT PATTERN, not a numeric value: the managed dispatcher packs one, the native adapter
/// unpacks it, the callback runs, and the result travels back the same way. Round-tripping an
/// arbitrary word is therefore the whole contract. These tests send the word for a value, run
/// a lambda that DOUBLES it, and require the word for twice that value.
///
/// <para><b>Why doubling rather than identity.</b> The first draft of this fixture used an
/// identity lambda and required the same word back — and it passed on every row, including the
/// broken ones. 1.5's bit pattern <c>0x3FF8000000000000</c> is <c>16376 × 2^48</c>: only 14
/// significant bits, so it is EXACTLY representable as a double and survives a value cast by
/// numeric accident. That is this task's recurring failure mode — an assertion green for a
/// structural reason rather than because the property holds — reproduced by the test written to
/// catch it. Doubling makes the expectation a value the wire must actually have carried.</para>
///
/// <para><b>⛔ Scope: this fixture pins the NATIVE half only</b> (<c>CppCodeGenerator.NetCalls</c>'s
/// adapter). It cannot see the managed half, because the stub runtime REPLACES the shim — a stub
/// writes its own packing, so a test built only on it would grade the fixture's convention rather
/// than <c>NetShimGenerator</c>'s. That is exactly how a native-only fix would look green while
/// being wrong; see <see cref="TheManagedHalfIsNotCoveredHere"/>, which states the other half and
/// fails if anyone assumes otherwise.</para>
///
/// <para>Chip <c>task_75064f2e</c>. <c>Double</c> and <c>Single</c> are expected RED until the
/// wire is fixed on BOTH halves together — see the fixture's closing remarks for why a partial
/// fix is worse than none.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class NetDelegateSlotWireTests
{
    /// <summary>
    /// A probe assembly, because no FRAMEWORK type offers a non-generic method taking a
    /// <c>Func</c> of a blittable scalar — the shapes that exist (<c>Enumerable.Select</c>,
    /// <c>Array.ConvertAll</c>) are generic methods, which §8.4 v1 does not carry.
    /// Each member hands the callback a value and returns what it answers, so the BasicLang
    /// side can be an identity lambda and the stub sees the full round trip.
    /// </summary>
    private const string ProbeSource = """
        namespace Wire.Probe
        {
            public static class Slots
            {
                public static double Dbl(System.Func<double, double> f) => f(0.0);
                public static float Flt(System.Func<float, float> f) => f(0.0f);
                public static int I32(System.Func<int, int> f) => f(0);
                public static long I64(System.Func<long, long> f) => f(0L);
            }
        }
        """;

    private static ProbeAssembly _probe;
    private static NetTypeResolver _resolver;

    [OneTimeSetUp]
    public void BuildProbe()
    {
        _probe = new ProbeAssembly("WireProbe", ProbeSource);
        _resolver = NetTypeResolver.Create(
            NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { _probe.Path }));
    }

    [OneTimeTearDown]
    public void DropProbe() => _probe?.Dispose();

    // ------------------------------------------------------------------------------------
    // The row table. Every row is a §8.3 scalar §8.4 admits, paired with a 64-bit WORD and the
    // reason that word is the interesting one.
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// <para><c>Word</c> is what the managed dispatcher must put on the wire for a value of this
    /// row, and therefore what an identity callback must hand back.</para>
    ///
    /// <para>The two floating rows carry an IEEE bit pattern — 1.5 is
    /// <c>0x3FF8000000000000</c> as a double and <c>0x3FC00000</c> as a float. Those words are
    /// enormous read as integers (4.6e18 and 1.06e9), which is the point: a conversion-based
    /// wire mangles them, a bit-preserving one does not. The integer rows are the CONTROLS —
    /// for them a value cast and a bit cast agree, so they must stay green under the fix and
    /// prove the harness itself is sound.</para>
    /// </summary>
    private static IEnumerable<TestCaseData> WireRows()
    {
        // Sent word, expected word back. The callback DOUBLES its argument (`v + v`), so the
        // expectation is a value the wire has to have carried correctly — not the word itself.
        yield return new TestCaseData("Dbl", "Double", "double",
                0x3FF8000000000000UL, 0x4008000000000000UL)
            .SetName("ADelegateWireCarriesTheValue(Double 1.5 -> 3.0)");
        yield return new TestCaseData("Flt", "Single", "float",
                0x3FC00000UL, 0x40400000UL)
            .SetName("ADelegateWireCarriesTheValue(Single 1.5f -> 3.0f)");
        yield return new TestCaseData("I32", "Integer", "int32_t", 21UL, 42UL)
            .SetName("ADelegateWireCarriesTheValue(Int32 21 -> 42 CONTROL)");
        yield return new TestCaseData("I64", "Long", "int64_t",
                1234567890123UL, 2469135780246UL)
            .SetName("ADelegateWireCarriesTheValue(Int64 CONTROL)");
    }

    /// <summary>
    /// §8.4's wire must round-trip the WORD. The stub puts <paramref name="word"/> on the wire,
    /// the BasicLang lambda is the identity, and the stub prints what came back — so anything
    /// other than the same word is the adapter's conversion corrupting a value it was only
    /// supposed to carry.
    ///
    /// <para>For the floating rows this fails TODAY, and the arithmetic says why:
    /// <c>static_cast&lt;double&gt;(0x3FF8000000000000)</c> is 4.6e18 — a value far past the 53
    /// bits a double's mantissa holds, so the return leg's
    /// <c>static_cast&lt;uint64_t&gt;</c> cannot give the word back. The bits are gone before
    /// the lambda ever runs.</para>
    /// </summary>
    [TestCaseSource(nameof(WireRows))]
    public void ADelegateWireCarriesTheValue(
        string member, string blType, string cWire, ulong word, ulong expected)
    {
        var (cpp, surface) = NetStubHarness.CompileWithSurface($"""
            Using Wire.Probe

            Module M
             Sub Main()
              Slots.{member}(Function(v As {blType}) v + v)
              Console.WriteLine("DONE")
             End Sub
            End Module
            """, optimize: false, resolver: _resolver);

        // Provenance from the SURFACE the collector actually built, not a re-resolution: this is
        // the descriptor production mangles, so a mangler or overload-selection change churns
        // the expectation automatically instead of this fixture disagreeing with the shim.
        var descriptor = surface.Members.Single(m => m.Name == member);
        var slot = NetNameMangler.Mangle(descriptor);

        // The stub stands in for the managed dispatcher, so it packs the way NetShimGenerator
        // MUST: a bit pattern, carried verbatim. It then prints the word that came back.
        var stub = NetStubHarness.StubTranslationUnit(new[]
        {
            new NetStubHarness.StubSlot(slot,
                $"[](uint64_t cb, {cWire}* result) -> int32_t {{"
                + $" uint64_t w = {word}ULL; uint64_t back = 0;"
                + " int32_t st = BasicLang::blnet::blnet_invoke_callback(cb, &w, 1, &back);"
                + " std::printf(\"WIRE st=%d back=%llu\\n\", (int)st, (unsigned long long)back);"
                + $" *result = ({cWire})0; return 0; }}"),
        });

        var output = NetStubHarness.RunWithStub(cpp, surface, stub).Replace("\r\n", "\n");

        Assert.That(output, Does.Contain($"WIRE st=0 back={expected}\n"),
            $"§8.4's delegate wire did not carry the {blType} row's VALUE. The wire moves a "
            + "64-bit WORD — a bit pattern, not a number — so a callback that doubles its "
            + $"argument must answer the word for twice the value. Sent {word}, expected "
            + $"{expected} back, got the line above.\n\n"
            + "For Double/Single this is chip task_75064f2e: CppCodeGenerator.NetCalls emits "
            + "`static_cast<T>(blnet_a[i])` on the way in and `static_cast<uint64_t>(r)` on the "
            + "way out, which CONVERTS the value where the row needs its bits reinterpreted. "
            + "⛔ Do NOT fix only this half — NetShimGenerator packs with `unchecked((ulong)a)` "
            + "and unpacks with `unchecked((T)r_)`, so the two halves are consistently lossy "
            + "today. Making C++ bit-exact alone leaves the shim sending an ALREADY-truncated "
            + "1, which bit_casts to 4.9e-324 — worse than the current answer. All four "
            + "conversion sites move together or none do.\n\nFull output:\n" + output);
    }

    /// <summary>
    /// The MANAGED half of the wire — the half the round-trip tests above physically CANNOT
    /// see, because the stub runtime replaces the shim.
    ///
    /// <para><b>Why this test has to exist.</b> All four conversion sites were value casts and
    /// mutually consistent, so the corruption began on the managed side and native code merely
    /// preserved it. Someone fixing only <c>CppCodeGenerator.NetCalls</c> would find every row
    /// above green and reasonably conclude the chip was closed — while the shim shipped an
    /// already-truncated <c>1</c> that a bit-exact adapter reinterprets as 4.9e-324, an answer
    /// worse than the defect. This asserts the managed half moved too.</para>
    ///
    /// <para>It reads the generated SOURCE rather than running it: an end-to-end managed proof
    /// needs the ~25 s AOT publish, which is <c>win-x64</c> only. So it is a weaker oracle than
    /// its neighbours and deliberately asserts only the tokens that decide the question. The
    /// end-to-end proof is <c>ADoubleDelegateSlot_TruncatesOnTheWire_PinnedDivergence</c>, which
    /// must be flipped from 2 to 3 on a Windows run.</para>
    /// </summary>
    [Test]
    public void TheManagedHalfCarriesFloatingBitsNotValues()
    {
        var exports = NetShimGenerator.Emit(
            NetSurfaceBuilderForDelegate(), "WireProbe")[NetShimGenerator.ExportsFileName];

        Assert.Multiple(() =>
        {
            Assert.That(exports, Does.Contain("DoubleToInt64Bits"),
                "the generated dispatcher must PACK a Double delegate argument by its bits. If "
                + "this is red, the managed half reverted to `unchecked((ulong)a)`, which makes "
                + "(ulong)1.5 == 1 — and the native seam then faithfully carries the wrong "
                + "number. Both halves move together (chip task_75064f2e).");
            Assert.That(exports, Does.Contain("Int64BitsToDouble"),
                "…and must UNPACK the return leg the same way. Packing bits while unpacking a "
                + "value is not half-fixed, it is a second defect.");
            Assert.That(exports, Does.Not.Contain("unchecked((ulong)a0)"),
                "a Double slot is still being packed by value cast. This surface's only delegate "
                + "takes a Double, so no admitted row here may use the integer spelling.");
        });
    }

    private static NetSurface NetSurfaceBuilderForDelegate()
    {
        var (_, surface) = NetStubHarness.CompileWithSurface("""
            Using Wire.Probe

            Module M
             Sub Main()
              Slots.Dbl(Function(v As Double) v)
              Console.WriteLine("DONE")
             End Sub
            End Module
            """, optimize: false, resolver: _resolver);
        return surface;
    }

    /// <summary>
    /// Compiles a C# probe assembly with Roslyn — the same shape
    /// <c>NetConversionRowLoweringTests</c> uses, kept private here for the same reason it is
    /// private there: it owns a temp directory whose lifetime is the fixture's.
    /// </summary>
    private sealed class ProbeAssembly : IDisposable
    {
        private readonly string _dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "blnet-wire-" + Guid.NewGuid().ToString("N"));

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
