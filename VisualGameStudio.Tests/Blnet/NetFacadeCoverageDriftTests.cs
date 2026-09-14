using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.CodeGen.Net;
using BasicLang.Compiler.ProjectSystem;
using BasicLang.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// §6's coverage invariant, asserted against a REAL framework surface
/// (plan <c>2026-09-13-blnet-cpp-facade.md</c>, Task 5).
///
/// <para><b>Why this exists when <c>NetFacadeEmitterTests</c> already asserts the set identity.</b>
/// That fixture asserts it over a PROBE assembly whose members were chosen, by me, to exercise the
/// arms I had already thought of. It proves the bookkeeping is self-consistent for shapes the
/// emitter was written against — and nothing whatsoever about the shapes .NET actually contains.
/// This is the plan's own §7.2 lesson turned on the test suite: a surface curated to be coverable
/// reports coverage it did not earn.</para>
///
/// <para><b>⛔ The hole this fixture closes.</b> A set identity is satisfied by skipping
/// EVERYTHING — <c>rendered = ∅</c>, <c>skipped = all</c>, every reason stated, identity holds,
/// green. Coverage therefore needs a FLOOR as well as an identity, which is what
/// <see cref="TheFacadeRendersTheLargeMajorityOfARealSurface"/> is for. Neither assertion is
/// sufficient alone: the identity catches a slot that falls through both buckets, the floor
/// catches a bucket that quietly swallows the surface.</para>
/// </summary>
[TestFixture]
public class NetFacadeCoverageDriftTests
{
    /// <summary>
    /// Four real framework types, chosen to reach more than one skip arm rather than for variety:
    /// <c>Int32</c> brings <c>TryParse</c>'s ByRef parameters and <c>TimeSpan</c> brings a
    /// multi-slot one, neither of which <c>Console</c> or <c>Regex</c> produces.
    ///
    /// <para>Several obvious candidates CANNOT appear here, and the reason is upstream of the
    /// facade: <c>NetProxyEmitter.Emit</c> throws BL6019 for them, so no surface containing one
    /// can be emitted at all. <c>StringBuilder</c> returns itself from <c>Clear()</c> (a §6.4
    /// by-value-pointer result), <c>DateTime</c> has <c>Deconstruct(out DateOnly, out TimeOnly)</c>
    /// and <c>Uri</c> has a constructor taking <c>in UriCreationOptions</c> — both ByRef handles,
    /// which §8.3 leaves unspecified. Adding one here fails this fixture with a BL6019 message
    /// that says nothing about facade coverage.</para>
    /// </summary>
    private static readonly string[] SurfaceTypes =
    {
        "System.Console",
        "System.Text.RegularExpressions.Regex",
        "System.Int32",
        "System.TimeSpan",
    };

    private static NetSurface _surface;

    [OneTimeSetUp]
    public void Collect()
    {
        var project = new ProjectFile();
        foreach (var type in SurfaceTypes) project.NetProxyTypes.Add(type);

        var resolver = NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths);
        _surface = NetSurfaceCollector.Collect(
            Array.Empty<BasicLang.Compiler.IR.IRModule>(), project, () => resolver,
            new List<NetReferenceDiagnostic>());

        Assert.That(_surface.IsNonEmpty, Is.True, "the framework probe must draw a surface");
    }

    private static IReadOnlyList<string> AllSlots() =>
        _surface.Members.Select(NetNameMangler.Mangle).Distinct(StringComparer.Ordinal).ToList();

    // ------------------------------------------------------------------------------------

    /// <summary>
    /// §6, on a surface nobody curated: every slot is either rendered or on the skip list with a
    /// stated reason. A set difference, not a count — a count passes while one slot swaps for
    /// another.
    /// </summary>
    [Test]
    public void EveryRealSlotIsEitherRenderedOrSkippedWithAReason()
    {
        var all = AllSlots();
        var rendered = NetProxyEmitter.FacadeRendered(_surface);
        var skipped = NetProxyEmitter.FacadeSkips(_surface);

        Assert.That(all, Has.Count.GreaterThan(100),
            "guard: the framework surface collapsed, so anything below proves nothing.");

        Assert.That(rendered.Concat(skipped.Select(s => s.SlotName)), Is.EquivalentTo(all),
            "a real framework slot is neither rendered in the facade nor on its skip list. Both "
            + "buckets come from the SAME Plan(surface) the proxy table is built from, so a slot "
            + "missing from both means the facade silently covers less than the surface.");

        Assert.That(skipped.Select(s => s.Reason), Has.All.Not.Empty,
            "a skip with no reason is exactly the silent omission this invariant exists to prevent.");
    }

    /// <summary>
    /// The FLOOR — the half of coverage a set identity cannot express.
    ///
    /// <para>Skipping every slot satisfies the identity perfectly. What stops that from passing is
    /// this: the facade must render the large majority of a real surface, and specific well-known
    /// members must be present by name. The ratio catches a new skip arm that swallows the surface
    /// wholesale; the named members catch one that targets a shape.</para>
    ///
    /// <para>The threshold is deliberately coarse (three quarters) and is NOT a measurement to be
    /// re-baselined when it drifts. Measured at the time of writing: 223 of 234 slots render, the
    /// 11 skips being nine ByRef parameters and two multi-slot ones. A drop to anywhere near the
    /// threshold means a shape stopped rendering and wants explaining, not a new number here.</para>
    /// </summary>
    [Test]
    public void TheFacadeRendersTheLargeMajorityOfARealSurface()
    {
        var all = AllSlots();
        var rendered = NetProxyEmitter.FacadeRendered(_surface);
        var text = NetProxyEmitter.Emit(_surface, "Drift.Blnet.dll")[NetProxyEmitter.FacadeFileName];

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Count * 4, Is.GreaterThan(all.Count * 3),
                $"the facade renders only {rendered.Count} of {all.Count} real slots. A set "
                + "identity cannot catch this — skipping EVERYTHING satisfies it — so the floor "
                + "is asserted separately. Find the skip arm that widened.");

            // Named members, one per shape the facade promises to render.
            Assert.That(text, Does.Contain("static void WriteLine(const char* a0);"),
                "a static method taking a String must render (D3).");
            Assert.That(text, Does.Contain("bool IsMatch(const char* a0) const;"),
                "an INSTANCE method must render with the receiver dropped (D3).");
            Assert.That(text, Does.Contain("Regex(const char* a0);"),
                "a constructor must render as a real C++ constructor (D5).");
            Assert.That(text, Does.Contain("static std::string Escape(const char* a0);"),
                "a String return must render as std::string.");
        });
    }

    /// <summary>
    /// The skip reasons are a CLOSED set: every omission on a real surface matches a category
    /// someone already wrote down.
    ///
    /// <para>This is the drift half of Task 5. A new <c>ClassifyForFacade</c> arm — or a collision
    /// rule that starts firing on the framework — changes what the facade covers, and the coverage
    /// test alone would stay green because the new skips carry reasons. Failing here forces the
    /// change to be a decision rather than a side effect.</para>
    ///
    /// <para>The list is the emitter's reasons verbatim, deliberately: matching on a loose keyword
    /// would keep passing through a rewrite that changed what the arm actually does.</para>
    /// </summary>
    [Test]
    public void EverySkipReasonIsOneOfTheKnownCategories()
    {
        string[] known =
        {
            "the result fans out to multiple scalar slots",
            "a parameter fans out to multiple scalar slots",
            "a ByRef parameter's ergonomic C++ spelling",
            "its declaring type's C++ name collides",
            "another slot renders to the SAME C++ signature",
        };

        var unknown = NetProxyEmitter.FacadeSkips(_surface)
            .Select(s => s.Reason)
            .Where(r => !known.Any(k => r.Contains(k, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.That(unknown, Is.Empty,
            "a skip category appeared that nobody recorded. That is a change in what the facade "
            + "covers, so it belongs in the plan's §4 and in this list — not in a reason string "
            + "the coverage test accepts silently. New reasons: " + string.Join(" | ", unknown));
    }

    /// <summary>
    /// The real framework surface produces NO collisions, and that is worth pinning rather than
    /// assuming.
    ///
    /// <para>It is the reason BL6027's own tests have to CONSTRUCT a collision: D7's wrapper types
    /// disambiguate most handle overloads and the rest differ in arity, so waiting to encounter one
    /// in the framework would have meant never testing the rule. If this ever goes red, the
    /// collision is real and the BL6027 path is finally being exercised for free — read the
    /// message before assuming it is a regression.</para>
    /// </summary>
    [Test]
    public void ARealFrameworkSurfaceProducesNoCollisions()
    {
        var diagnostics = NetProxyEmitter.FacadeDiagnostics(_surface);

        Assert.That(diagnostics, Is.Empty,
            "the framework surface now collides where it did not before. This is not necessarily a "
            + "defect — the facade correctly omits both sides and warns — but it changes what C++ "
            + "can name, so it must be a decision. Findings: "
            + string.Join(" | ", diagnostics.Select(d => d.Message)));
    }
}
