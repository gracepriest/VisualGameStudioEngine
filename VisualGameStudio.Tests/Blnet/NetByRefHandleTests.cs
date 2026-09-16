using System;
using System.Linq;
using BasicLang.Compiler.CodeGen.Net;
using BasicLang.Net;
using BlnetTestShim;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// §8.3's <b>ByRef handle ownership</b> rule, resolved 2026-09-15.
///
/// <para><b>What was actually missing.</b> Both halves of the wire already implemented this —
/// the shim writes back through <c>ToHandle</c>, the proxy takes a mutable <c>NetRef&amp;</c>
/// and adopts what it finds, and §8.6's <c>ref</c>/<c>out</c> ARRAY row has been exercising the
/// path since Task 10. What was missing was the SPEC saying so, and three gates refused the
/// shape because the spec did not, each repeating one premise: writing back would release a
/// handle the callee may have returned unchanged, so a double release.</para>
///
/// <para><b>That premise is false, and <see cref="HandleTable.Create"/> is why.</b> It allocates
/// a new slot, a new <c>GCHandle</c> and its own refcount on EVERY call, with no identity map —
/// so re-handling an object the caller already holds yields a SECOND independent table
/// reference, hence two independent releases rather than a double free. Note what this means
/// for §8.6's original justification, "the caller minted the incoming handle itself": that is
/// true but NOT load-bearing, because minting decides who releases what, not whether the
/// write-back is well defined. The load-bearing rule is the one §8.3 now states — the managed
/// side never writes back an incoming handle value.</para>
/// </summary>
[TestFixture]
public class NetByRefHandleTests
{
    // ====================================================================================
    // 1. The premise the whole rule rests on, asserted against the REAL table.
    // ====================================================================================

    /// <summary>
    /// Two <c>Create</c> calls on ONE object return two DIFFERENT handles, each independently
    /// releasable.
    ///
    /// <para>This is the fact that makes every ByRef handle write-back in the codebase safe. If
    /// <c>Create</c> ever grew an identity map — returning the live handle for an object it has
    /// already seen, as a "cheap" allocation saving — the proxy's <c>a0 = NetRef(written)</c>
    /// would adopt a handle the caller still owns, and the two releases that follow would be a
    /// genuine double release. Nothing else in the suite would catch that, because every other
    /// handle test uses a fresh object per call.</para>
    ///
    /// <para><c>BlnetTestShim.HandleTable</c> is not a mirror of the shipped table — the shipped
    /// text is asserted byte-equal to this file by
    /// <c>BlnetShimSourcesTests.HandleTableMatchesTheHandWrittenShimTheFrozenSuiteValidates</c>,
    /// so this runs the code the shim really carries.</para>
    /// </summary>
    [Test]
    public void CreateNeverReturnsAnExistingHandle_WhichIsWhatMakesTheWriteBackSafe()
    {
        var table = new HandleTable();
        var shared = new object();

        var first = table.Create(shared);
        var second = table.Create(shared);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.EqualTo(first),
                "Create must have NO identity map. If it returned the caller's live handle for "
                + "an object it has already handed out, a ByRef write-back would adopt a "
                + "reference the caller still owns — the double release §8.3's rule denies.");

            // The two references are INDEPENDENT: releasing one leaves the other usable. That
            // independence, not the mere inequality above, is what the rule consumes.
            Assert.That(table.Release(first), Is.EqualTo(BlnetStatus.BLNET_OK));
            Assert.That(table.TryGet(second, out var still), Is.EqualTo(BlnetStatus.BLNET_OK),
                "releasing one reference must not disturb the other.");
            Assert.That(still, Is.SameAs(shared));
            Assert.That(table.Release(second), Is.EqualTo(BlnetStatus.BLNET_OK),
                "…and the second release is an ordinary release, not a stale-handle error.");
        });
    }

    // ====================================================================================
    // 2. The gate: a ByRef handle is admitted, the three unresolved shapes are not.
    // ====================================================================================

    /// <summary>
    /// All FOUR ref kinds, not just <c>out</c>. <c>NetShimGenerator</c> writes
    /// <c>*aN = ToHandle(local)</c> for every ByRef slot — there is no <c>in</c> exception — so
    /// §8.3's rule has to hold for every kind or the ones it does not cover are unsound.
    ///
    /// <para><c>in</c> and <c>ref readonly</c> are the interesting pair: the managed side cannot
    /// have CHANGED the object, yet it still writes back a newly created handle for that same
    /// object, so the caller finishes holding a different handle than it passed. That looks
    /// wasteful and is in fact the case that proves the fresh-handle rule is load-bearing — a
    /// shim that "optimised" <c>in</c> by writing the incoming value straight back would make
    /// the adopting assignment release the caller's only reference and then keep a stale handle
    /// to a collected object.</para>
    /// </summary>
    /// <remarks>
    /// Spelled as NAMES rather than enum values because <c>NetRefKind</c> is internal and an
    /// NUnit test signature has to be public. <see cref="Enum.Parse{T}(string)"/> throws on an
    /// unknown name, so a rename fails loudly here rather than silently dropping a case; the
    /// companion guard below is what catches an ADDED one.
    /// </remarks>
    [TestCase("Out")]
    [TestCase("Ref")]
    [TestCase("In")]
    [TestCase("RefReadOnly")]
    public void AHandleParameterIsAdmittedForEveryRefKind(string refKindName)
    {
        var refKind = Enum.Parse<NetRefKind>(refKindName);

        Assert.DoesNotThrow(() => Emit(HandleMember(refKind)),
            "§8.3 specifies this shape as of the 2026-09-15 resolution. A throw here means the "
            + "gate was re-narrowed without the spec being re-opened.");
    }

    /// <summary>
    /// The four cases above are the WHOLE by-reference set, and this is what keeps that true.
    ///
    /// <para>Every ByRef kind gets the write-back — <c>ParameterPlan.ByRef</c> is
    /// <c>RefKind != None</c>, with no per-kind arm anywhere downstream — so a fifth kind added
    /// to the enum would inherit adoption semantics with nothing in §8.3 covering it and no test
    /// exercising it. Failing here is the prompt to decide what the new kind means before it
    /// ships, not a chore to silence by adding a name to the list.</para>
    /// </summary>
    [Test]
    public void TheRefKindsAboveAreTheCompleteByReferenceSet()
    {
        var byReference = Enum.GetNames<NetRefKind>()
            .Where(n => n != nameof(NetRefKind.None)).OrderBy(n => n, StringComparer.Ordinal);

        Assert.That(byReference, Is.EqualTo(new[] { "In", "Out", "Ref", "RefReadOnly" }),
            "NetRefKind grew or shrank. Each by-reference kind reaches the same write-back, so "
            + "a new one needs a §8.3 ruling and a case above before it can be waved through.");
    }

    /// <summary>
    /// <b>ByRef String stays refused, for a reason of its own.</b> Ownership runs opposite ways
    /// per direction — an in-parameter borrows the caller's buffer, an out-parameter transfers a
    /// <c>blnet_alloc</c> one the caller must free — and a single <c>char**</c> cannot carry
    /// both. That is unrelated to the handle question, so the handle resolution must not widen
    /// to it by analogy.
    /// </summary>
    [Test]
    public void AByRefStringIsStillRefused_ForItsOwnReason()
    {
        var ex = Assert.Throws<NotSupportedException>(() => Emit(ByRefMember("System.String")));

        Assert.That(ex!.Message, Does.Contain("ByRef String"),
            "the refusal must still name String specifically — its open question is ownership "
            + "direction, not the write-back safety §8.3 just settled.");
    }

    /// <summary>
    /// <b>ByRef §6.4 stays refused, for a reason of its own.</b> A §6.4 row crosses as a pointer
    /// to a buffer the MANAGED side marshals a copy out of — so "the callee writes through your
    /// pointer" is not what happens, and a write-back would need a re-marshal contract §8.3 does
    /// not define.
    /// </summary>
    [Test]
    public void AByRef64Row_IsStillRefused_ForItsOwnReason()
    {
        var ex = Assert.Throws<NotSupportedException>(() => Emit(ByRefMember("System.Guid")));

        Assert.That(ex!.Message, Does.Contain("§6.4"),
            "a Guid crosses as a by-value pointer; the handle resolution says nothing about it.");
    }

    /// <summary>
    /// <b>ByRef enum stays refused, for a reason of its own</b> — and by a DIFFERENT guard, one
    /// that must keep running ahead of the wire-kind check. §8.3 crosses an enum as its
    /// underlying integral, which is a Scalar wire; now that Scalar and Handle are both admitted
    /// above, a ByRef enum that reached the wire-kind check would be silently accepted and get
    /// an <c>int32_t*</c> slot with no widening contract behind it.
    /// </summary>
    [Test]
    public void AByRefEnumIsStillRefused_ByTheGuardThatRunsAheadOfTheWireKindCheck()
    {
        var member = new NetMemberDescriptor(
            "TakesRefEnum", "MyLib.Api", NetMemberCategory.Method, isStatic: true, arity: 0,
            "System.Void",
            new[]
            {
                new NetParameterDescriptor(NetRefKind.Ref, "System.IO.FileMode",
                    EnumUnderlyingTypeFullName: "System.Int32"),
            });

        var ex = Assert.Throws<NotSupportedException>(() => Emit(member));

        Assert.That(ex!.Message, Does.Contain("ByRef enum"),
            "the enum guard must still fire. It is the only thing standing in front of a shape "
            + "the widened wire-kind check would now wave through.");
    }

    // ====================================================================================
    // 3. The emitted slot.
    // ====================================================================================

    /// <summary>
    /// The slot takes a MUTABLE <c>NetRef&amp;</c> and adopts the callee's handle.
    ///
    /// <para><c>const NetRef&amp;</c> is the failure mode worth naming: it compiles — the
    /// parameter binds fine, and reference collapsing hides the mistake — and then the
    /// write-back cannot be assigned, so the callee's handle is dropped and the caller keeps a
    /// stale one.</para>
    /// </summary>
    [Test]
    public void TheSlotTakesAMutableNetRef_AndAdoptsTheHandleTheCalleeWrote()
    {
        var proxies = Emit(OutHandleMember())[NetProxyEmitter.ProxiesFileName];

        Assert.Multiple(() =>
        {
            Assert.That(proxies, Does.Contain("BasicLang::blnet::NetRef& a0"),
                "the ByRef handle parameter must be a MUTABLE reference…");
            Assert.That(proxies, Does.Not.Contain("const BasicLang::blnet::NetRef& a0"),
                "…never const, or the write-back has nowhere to land.");
            Assert.That(proxies, Does.Contain("uint64_t blnet_a0 = a0.get();"),
                "the caller's current handle crosses by value…");
            Assert.That(proxies, Does.Contain("a0 = BasicLang::blnet::NetRef(blnet_a0);"),
                "…and whatever the callee wrote is ADOPTED, the assignment releasing the "
                + "caller's old reference exactly once.");
        });
    }

    // ====================================================================================
    // 4. The run proof.
    // ====================================================================================

    /// <summary>
    /// <b>The run oracle.</b> Emission can show the write-back is SPELLED correctly; only
    /// running can show what it does to the two table references involved.
    ///
    /// <para>The stub writes a handle DIFFERENT from the one it received — which is what a real
    /// shim does, since <c>ToHandle</c> goes through <c>Create</c> and
    /// <see cref="CreateNeverReturnsAnExistingHandle_WhichIsWhatMakesTheWriteBackSafe"/> pins
    /// that <c>Create</c> never hands back an existing handle. An ECHO stub would have been the
    /// weaker choice and the repo has been bitten by it before: echoing proves only that a value
    /// survived the trip, and would pass just as happily against a proxy that never wrote back
    /// at all.</para>
    ///
    /// <para><b>What makes this a double-release oracle and not just an adoption check.</b> The
    /// stub also fills <c>g_shim.release</c>, which is where <c>NetRef</c>'s deleter lands, so
    /// the assertion covers the exact release SEQUENCE: the caller's original released once when
    /// the write-back is adopted, the callee's released once when the caller's <c>NetRef</c>
    /// goes out of scope. A proxy that released the adopted handle instead of the old one, or
    /// released either twice, changes this transcript.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AtRunTime_TheCalleesHandleIsAdopted_AndEachReferenceIsReleasedExactlyOnce()
    {
        var member = OutHandleMember();
        var slot = NetNameMangler.Mangle(member);

        var stub = NetStubHarness.StubTranslationUnit(
            new[]
            {
                new NetStubHarness.StubSlot(slot,
                    "[](uint64_t* a0) -> int32_t {"
                    + " std::printf(\"IN:%llu\\n\", (unsigned long long)*a0);"
                    + " *a0 = 777; return 0; }"),
            },
            shimSetup:
            "        BasicLang::blnet::g_shim.release = [](uint64_t h) -> int32_t {"
            + " std::printf(\"REL:%llu\\n\", (unsigned long long)h); return 0; };");

        var main = """
            #include "blnet_proxies.g.hpp"
            #include <cstdio>

            int main() {
                BasicLang::blnet::NetRef h(42);
                BasicLang::net::__SLOT__(h);
                std::printf("OUT:%llu\n", (unsigned long long)h.get());
                return 0;
            }
            """.Replace("__SLOT__", slot);

        var output = NetStubHarness.RunWithStub(
            main, new NetSurface(new[] { member }, Array.Empty<string>()), stub)
            .Replace("\r\n", "\n");

        Assert.That(output, Is.EqualTo("IN:42\nREL:42\nOUT:777\nREL:777\n"),
            "IN must be the CALLER's handle (the slot receives the current value, it is not an "
            + "out-only slot); REL:42 is the caller's reference dropped by the adopting "
            + "assignment; OUT:777 is the callee's handle now owned by the caller — 'OUT:42' "
            + "would mean the write-back was dropped entirely; REL:777 is that adopted "
            + "reference released once at scope exit. A missing or duplicated REL line is the "
            + "double release §8.3's rule denies is possible.");
    }

    // ====================================================================================
    // Fixture helpers.
    // ====================================================================================

    private static System.Collections.Generic.IReadOnlyDictionary<string, string> Emit(
        NetMemberDescriptor member) =>
        NetProxyEmitter.Emit(new NetSurface(new[] { member }, Array.Empty<string>()), "P.dll");

    /// <summary>A static method taking one <c>out</c> parameter of a handle-represented type.</summary>
    internal static NetMemberDescriptor OutHandleMember() => HandleMember(NetRefKind.Out);

    private static NetMemberDescriptor HandleMember(NetRefKind refKind) =>
        new("TakesOutHandle", "MyLib.Api", NetMemberCategory.Method, isStatic: true, arity: 0,
            "System.Void",
            new[] { new NetParameterDescriptor(refKind, "MyLib.Widget") });

    private static NetMemberDescriptor ByRefMember(string typeFullName) =>
        new("TakesRef", "MyLib.Api", NetMemberCategory.Method, isStatic: true, arity: 0,
            "System.Void",
            new[] { new NetParameterDescriptor(NetRefKind.Ref, typeFullName) });
}
