using System;
using System.Linq;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.Net;
using BasicLang.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// P2a-2 Task 11 (§8.4 delegates), Steps 1-2: a .NET member's delegate-typed parameter must be
/// ADMISSIBLE at the boundary and must CARRY its invoke signature onto the surface.
///
/// <para>Why the signature has to be carried rather than re-derived (decision D-P9): neither
/// consumer can recover it. <c>NetShimGenerator</c> imports no <c>Microsoft.CodeAnalysis</c> at
/// all and its <c>Emit</c> signature takes no resolver; <c>NetProxyEmitter.WireOf</c> sees only a
/// type NAME. The single place a descriptor is built from a Roslyn symbol is
/// <c>NetTypeResolver.Describe</c>, so that is where it is populated.</para>
/// </summary>
[TestFixture]
public class NetDelegateTests
{
    /// <summary>One resolver for the fixture — construction reads ~170 assemblies.</summary>
    private static readonly Lazy<NetTypeResolver> SharedResolver =
        new(() => NetTypeResolver.Create(NetTypeResolverTestRefs.FrameworkPaths));

    /// <summary>
    /// <c>Regex.Replace(String, MatchEvaluator)</c> — a NON-GENERIC delegate parameter that really
    /// exists in the framework, so the test does not also depend on Task 8b's generic-arity work.
    /// <c>MatchEvaluator</c> is <c>delegate String MatchEvaluator(Match match)</c>.
    /// </summary>
    private static NetParameterDescriptor MatchEvaluatorParameter()
    {
        var members = SharedResolver.Value.GetMembers("System.Text.RegularExpressions.Regex");
        var replace = members.First(m =>
            m.Name == "Replace"
            && m.Parameters.Count == 2
            && m.Parameters[1].TypeFullName == "System.Text.RegularExpressions.MatchEvaluator");
        return replace.Parameters[1];
    }

    [Test]
    public void DelegateParameter_CarriesItsInvokeSignature()
    {
        var evaluator = MatchEvaluatorParameter();

        Assert.That(evaluator.DelegateInvokeSignature, Is.EqualTo(
                "System.String(System.Text.RegularExpressions.Match)"),
            "§8.4 needs the invoke signature to compute BlnetSlotDesc[] and to build the managed "
            + "delegate. A type NAME cannot answer it, and neither emitter has a resolver.");
    }

    [Test]
    public void NonDelegateParameter_CarriesNoInvokeSignature()
    {
        // Anti-vacuity partner: without this, "every parameter carries a signature" would pass
        // the test above just as well as the correct behaviour does.
        var members = SharedResolver.Value.GetMembers("System.Text.RegularExpressions.Regex");
        var replace = members.First(m =>
            m.Name == "Replace"
            && m.Parameters.Count == 2
            && m.Parameters[1].TypeFullName == "System.Text.RegularExpressions.MatchEvaluator");

        Assert.That(replace.Parameters[0].TypeFullName, Is.EqualTo("System.String"),
            "guard: this test is worthless if it is not looking at the String parameter");
        Assert.That(replace.Parameters[0].DelegateInvokeSignature, Is.Null,
            "only delegate-typed parameters carry a signature");
    }

    /// <summary>
    /// D-P9's trap, and the reason the field is additive-and-scalar rather than convenient.
    ///
    /// <para><c>NetNameMangler.Mangle</c> builds its readable stem from
    /// <c>NetParameterDescriptor.ToString()</c> and its hash from <c>CanonicalIdentity</c>. If
    /// EITHER learns about the new field, every mangled export name changes — and an export name
    /// is simultaneously the proxy-table slot, the shim's <c>EntryPoint</c> string and the shim
    /// cache key. A live cache would silently serve shims whose exports no longer match.</para>
    /// </summary>
    [Test]
    public void CarryingAnInvokeSignature_DoesNotChangeTheMangledExportName()
    {
        var withSignature = new NetParameterDescriptor(
            NetRefKind.None, "System.Action", "System.Void()");
        var withoutSignature = new NetParameterDescriptor(NetRefKind.None, "System.Action");

        Assert.That(withSignature.ToString(), Is.EqualTo(withoutSignature.ToString()),
            "ToString feeds Mangle's readable stem — it must not see the signature");

        var member = new Func<NetParameterDescriptor, NetMemberDescriptor>(p =>
            new NetMemberDescriptor(
                "Contoso.Widget", "Run", NetMemberCategory.Method, isStatic: true, arity: 0,
                "System.Void", new[] { p }));

        Assert.That(NetNameMangler.Mangle(member(withSignature)),
            Is.EqualTo(NetNameMangler.Mangle(member(withoutSignature))),
            "the export name is the proxy slot, the shim EntryPoint AND part of the cache key");
    }

    /// <summary>
    /// The field must stay a SCALAR. Record equality over a list member degenerates to REFERENCE
    /// equality — the exact trap <c>NetMemberDescriptor</c> and <c>NetReferenceClosure</c> both
    /// document, and the reason this carries a rendered signature string rather than a list.
    /// </summary>
    [Test]
    public void ParameterDescriptorEquality_StaysValueEquality()
    {
        var a = new NetParameterDescriptor(NetRefKind.None, "System.Action", "System.Void()");
        var b = new NetParameterDescriptor(NetRefKind.None, "System.Action", "System.Void()");

        Assert.That(a, Is.EqualTo(b), "two descriptions of the same parameter must be equal");
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    // ------------------------------------------------------------------------------------
    // Step 1 — admissibility. Decision D-P11: a lambda is TARGET-TYPED against each candidate,
    // exactly as C# does it, rather than being given an independent .NET type. A BasicLang
    // lambda types as Func/Action with generic arguments, while real .NET delegate parameters
    // are NAMED types (MatchEvaluator, Comparison(Of T), ThreadStart) — so nominal matching
    // admits none of the APIs anyone actually calls.
    //
    // The probe already delegates overload resolution to the real C# compiler by synthesizing
    // source, so a lambda argument becomes a lambda EXPRESSION in that source and Roslyn does
    // the target-typing for us.
    // ------------------------------------------------------------------------------------

    [Test]
    public void ALambdaArgument_TargetTypesOntoADelegateParameter()
    {
        // Regex.Replace(String, MatchEvaluator) — MatchEvaluator takes exactly one parameter.
        var result = SharedResolver.Value.ResolveOverload(
            "System.Text.RegularExpressions.Regex",
            NetCallForm.Instance,
            "Replace",
            new[] { "System.String", NetTypeResolver.LambdaArgumentSpelling(1) });

        Assert.That(result.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "a one-parameter lambda must select the MatchEvaluator overload");
        Assert.That(result.Member.Parameters[1].TypeFullName,
            Is.EqualTo("System.Text.RegularExpressions.MatchEvaluator"));
    }

    [Test]
    public void ALambdaOfTheWrongArity_DoesNotMatch()
    {
        // THE anti-vacuity guard for target-typing. Without it, "every lambda matches every
        // delegate parameter" would pass the test above just as well as real target-typing —
        // and would then select nonsense overloads for every user program.
        var result = SharedResolver.Value.ResolveOverload(
            "System.Text.RegularExpressions.Regex",
            NetCallForm.Instance,
            "Replace",
            new[] { "System.String", NetTypeResolver.LambdaArgumentSpelling(3) });

        Assert.That(result.Outcome, Is.EqualTo(NetOverloadOutcome.NoMatch),
            "MatchEvaluator takes one parameter; a three-parameter lambda is not convertible");
    }

    [Test]
    public void ALambdaArgument_SelectsAVoidReturningDelegateToo()
    {
        // `throw null!` as the synthesized lambda body is what makes ONE spelling serve both
        // Action-shaped (void) and Func-shaped (value-returning) targets — a `default`-bodied
        // lambda would not convert to a void-returning delegate.
        var result = SharedResolver.Value.ResolveOverload(
            "System.Threading.Thread",
            NetCallForm.Constructor,
            ".ctor",
            new[] { NetTypeResolver.LambdaArgumentSpelling(0) });

        Assert.That(result.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "ThreadStart is a zero-parameter VOID delegate");
        Assert.That(result.Member.Parameters[0].TypeFullName,
            Is.EqualTo("System.Threading.ThreadStart"));
    }

    // ------------------------------------------------------------------------------------
    // Step 3 — ONE shared "required delegate forms" derivation, following the
    // NetArrayCopy.RequiredForms pattern. Forced by §12.4: the proxy table's slots must EQUAL
    // the shim's surface-derived exports, and NetShimGenerator.cs:68-74 rules out an exemption
    // for the delegate dispatcher in as many words. Both emitters must call ONE function, so
    // "slots ≡ exports" is a consequence of calling it twice rather than a property to re-check.
    // ------------------------------------------------------------------------------------

    // NetMemberDescriptor's first argument is the MEMBER NAME, the second the declaring type.
    private static NetMemberDescriptor MemberTaking(string name, params string[] parameterTypes) =>
        new(name, "Contoso.Widget", NetMemberCategory.Method, isStatic: true, arity: 0,
            "System.Void",
            parameterTypes.Select(t => new NetParameterDescriptor(
                NetRefKind.None, t, DelegateSignatureFor(t))).ToArray());

    /// <summary>Null for a non-delegate spelling, so these fixtures exercise both arms.</summary>
    private static string DelegateSignatureFor(string typeFullName) => typeFullName switch
    {
        "System.Action" => "System.Void()",
        "System.Threading.ThreadStart" => "System.Void()",
        "System.Text.RegularExpressions.MatchEvaluator" =>
            "System.String(System.Text.RegularExpressions.Match)",
        _ => null,
    };

    [Test]
    public void RequiredForms_YieldsOneFormPerDistinctDelegateInTheSurface()
    {
        var surface = new NetSurface(
            new[]
            {
                MemberTaking("A", "System.Action", "System.Int32"),
                MemberTaking("B", "System.Action"),   // same delegate again — must not duplicate
                MemberTaking("C", "System.Text.RegularExpressions.MatchEvaluator"),
            },
            Array.Empty<string>());

        var forms = NetDelegateDispatch.RequiredForms(surface);

        Assert.That(forms.Select(f => f.DelegateFullName), Is.EquivalentTo(new[]
        {
            "System.Action",
            "System.Text.RegularExpressions.MatchEvaluator",
        }), "one form per DISTINCT delegate type; non-delegate parameters contribute nothing");
    }

    [Test]
    public void RequiredForms_OrderIsDeterministicNotEncounterOrder()
    {
        // The emitted export set is part of §10.2's shim cache key. An order that depended on IR
        // walk order would produce false cache MISSES — a ~27 s republish for an unchanged
        // surface — and, worse, two orders for one surface break §12.4's set comparison the
        // moment either emitter is asked to enumerate rather than compare.
        NetSurface Surface(params string[] order) =>
            new(order.Select(d => MemberTaking("M" + d, d)).ToArray(), Array.Empty<string>());

        var forwards = NetDelegateDispatch.RequiredHelperNames(Surface(
            "System.Action", "System.Threading.ThreadStart",
            "System.Text.RegularExpressions.MatchEvaluator"));
        var backwards = NetDelegateDispatch.RequiredHelperNames(Surface(
            "System.Text.RegularExpressions.MatchEvaluator", "System.Threading.ThreadStart",
            "System.Action"));

        Assert.That(forwards, Is.Not.Empty);
        Assert.That(backwards, Is.EqualTo(forwards).AsCollection,
            "the same surface in a different walk order must produce the SAME export sequence");
    }

    [Test]
    public void TwoDelegatesSharingAnInvokeSignature_GetDistinctExports()
    {
        // System.Action and System.Threading.ThreadStart are both `void()`. The managed
        // dispatcher has to construct the RIGHT named delegate, so forms are keyed on the
        // delegate TYPE, never on its signature — and their exports must not collide.
        var surface = new NetSurface(
            new[] { MemberTaking("A", "System.Action", "System.Threading.ThreadStart") },
            Array.Empty<string>());

        var names = NetDelegateDispatch.RequiredHelperNames(surface);

        Assert.That(names.Count, Is.EqualTo(2), "two distinct delegate types, two exports");
        Assert.That(names.Distinct().Count(), Is.EqualTo(names.Count),
            "identical invoke signatures must NOT collapse to one export");
    }

    [Test]
    public void ASurfaceWithNoDelegates_RequiresNoForms()
    {
        var surface = new NetSurface(
            new[] { MemberTaking("A", "System.Int32", "System.String") },
            Array.Empty<string>());

        Assert.That(NetDelegateDispatch.RequiredForms(surface), Is.Empty);
        Assert.That(NetDelegateDispatch.RequiredHelperNames(surface), Is.Empty);
    }

    /// <summary>
    /// §12.4 under a delegate-bearing surface, and the guard for the correction that produced
    /// <see cref="NetDelegateForm.HelperName"/>'s name.
    ///
    /// <para>The dispatcher is a MANAGED helper called from inside a member wrapper — the native
    /// side mints its callback handle through <c>blnet_register_callback</c>, which is native
    /// runtime, not a shim export. No native caller means no proxy slot and no export, so adding
    /// a delegate parameter must leave "slots ≡ exports" completely untouched. Appending helper
    /// names to either side — the obvious reading of "not §12.4-exempt" — would break the very
    /// invariant that warning protects, and this test is what catches it.</para>
    /// </summary>
    [Test]
    public void ADelegateBearingSurface_KeepsSlotsAndExportsEqual()
    {
        var surface = new NetSurface(
            new[] { MemberTaking("Run", "System.Action") },
            Array.Empty<string>());

        var helpers = NetDelegateDispatch.RequiredHelperNames(surface);
        Assert.That(helpers, Is.Not.Empty,
            "guard: this test is worthless unless the surface really did require a dispatcher");

        var slots = NetProxyEmitter.EmitBindings(surface).SlotNames;
        var exports = NetShimGenerator.SurfaceDerivedExportNames(surface);

        Assert.That(slots, Is.EquivalentTo(exports),
            "§12.4 must still hold once a delegate parameter is in the surface");
        Assert.That(exports, Has.No.Member(helpers[0]),
            "a managed dispatcher is not an export and must not appear in the export set");
    }

    // ------------------------------------------------------------------------------------
    // Step 4a — the proxy layer's callback wire row.
    // ------------------------------------------------------------------------------------

    [Test]
    public void ADelegateParameter_IsSpelledAsACallbackHandle_NotANetRef()
    {
        var surface = new NetSurface(
            new[] { MemberTaking("Run", "System.Action") },
            Array.Empty<string>());

        var proxies = NetProxyEmitter.Emit(surface, "Shim.dll")[NetProxyEmitter.ProxiesFileName];

        Assert.That(proxies, Does.Contain("BasicLang::blnet::blnet_callback"),
            "a delegate parameter's C++ spelling is a callback handle");

        // The failure this pins is not a compile error, it is a WRONG-TABLE RELEASE. NetRef's
        // deleter routes to g_shim.release — the MANAGED object table. A callback handle lives in
        // the NATIVE callback table and is freed by blnet_callback_release. Wrapping one in a
        // NetRef would release an unrelated managed object and leave the callback entry alive.
        Assert.That(proxies, Does.Not.Contain("const BasicLang::blnet::NetRef&"),
            "a callback handle must never be spelled NetRef — different table, different release");
    }

    // ------------------------------------------------------------------------------------
    // Step 4b — the managed dispatcher.
    //
    // v1 scope: delegates whose Invoke parameters and return are BLITTABLE SCALARS. That is
    // exactly the plan's mandatory shape (Comparison(Of T) inside List.Sort) and P0 conformance
    // scenario 8's proven shape — VALUE slots plus a scalar written to *result. Handle-, String-
    // and struct-slotted delegates are refused loudly rather than guessed at, because the
    // failure mode of guessing is a wrong-table read at runtime that no compile step catches.
    // ------------------------------------------------------------------------------------

    private static NetSurface ComparisonSurface() => new(
        new[]
        {
            new NetMemberDescriptor(
                "Sort", "Contoso.Widget", NetMemberCategory.Method, isStatic: true, arity: 0,
                "System.Void",
                new[]
                {
                    // TypeFullName is C# SYNTAX, fully qualified and never shorthand — that is
                    // NetTypeResolver.TypeName's contract, and the reason Qualified() can simply
                    // prefix "global::".
                    new NetParameterDescriptor(
                        NetRefKind.None, "System.Comparison<System.Int32>",
                        "System.Int32(System.Int32,System.Int32)"),
                }),
        },
        Array.Empty<string>());

    [Test]
    public void TheShimEmitsADispatcherForEachRequiredDelegate()
    {
        var surface = ComparisonSurface();
        var helper = NetDelegateDispatch.RequiredHelperNames(surface).Single();

        var exports = NetShimGenerator.Emit(surface, "Shim")[NetShimGenerator.ExportsFileName];

        Assert.That(exports, Does.Contain(helper),
            "the shim must emit a dispatcher named by the ONE shared derivation");
        Assert.That(exports, Does.Contain("_thunk"),
            "the dispatcher is what finally reads the vtable slot P2a-1 has been storing unused");
    }

    [Test]
    public void ADelegateArgument_IsDecodedByTheDispatcher_NotTheHandleTable()
    {
        // The bug this pins: a callback handle belongs to the NATIVE callback table. Decoding it
        // through the managed HandleTable would fail Validate and surface as BLNET_E_STALE_HANDLE
        // — a confusing error for a perfectly good callback.
        var surface = ComparisonSurface();
        var helper = NetDelegateDispatch.RequiredHelperNames(surface).Single();

        var exports = NetShimGenerator.Emit(surface, "Shim")[NetShimGenerator.ExportsFileName];

        Assert.That(exports, Does.Match(@"Widget\.Sort\s*\(\s*" + helper + @"\("),
            "the wrapper must pass the delegate built by the dispatcher, not a Table.TryGet cast");
    }

    // ------------------------------------------------------------------------------------
    // Step 5a — the callback RAII guard.
    //
    // Registration belongs in the call PROLOGUE as an RAII guard (D-P8), never in WriteBack:
    // WriteBack is skipped whenever NetCheckTyped throws, so a throwing managed callee would
    // leak the callback entry permanently — the entry is only reclaimed through the freelist
    // in blnet_callback_release.
    // ------------------------------------------------------------------------------------

    [Test]
    public void TheRuntimeShipsACallbackGuard_ReleasingThroughTheCallbackTable()
    {
        var runtime = BlnetRuntimeSources.BlnetRuntime;

        Assert.That(runtime, Does.Contain("class CallbackRef"),
            "§8.4 needs a scope-lifetime holder for a callback handle");
        Assert.That(runtime, Does.Match(@"~CallbackRef\(\)[^}]*blnet_callback_release"),
            "the destructor must release through the NATIVE callback table — NOT g_shim.release, "
            + "which frees a MANAGED object handle and would leave the callback entry alive");
    }

    [Test]
    public void TheCallbackGuard_IsNonCopyable()
    {
        // A copy would double-release: blnet_callback_release bumps the generation and pushes
        // the index onto the freelist, so the second release either reports
        // BLNET_E_STALE_CALLBACK or — worse, after the index is reused — frees a stranger's
        // callback. Move-only is the only safe shape.
        var runtime = BlnetRuntimeSources.BlnetRuntime;

        Assert.That(runtime, Does.Match(@"CallbackRef\(const CallbackRef&\)\s*=\s*delete"),
            "copy construction must be deleted");
        Assert.That(runtime, Does.Match(@"CallbackRef&\s*operator=\(const CallbackRef&\)\s*=\s*delete"),
            "copy assignment must be deleted");
    }

    // ------------------------------------------------------------------------------------
    // Step 5b — BlnetSlotDesc[] from the invoke signature.
    // ------------------------------------------------------------------------------------

    [Test]
    public void TheInvokeSignatureParser_SplitsReturnAndParameters()
    {
        Assert.That(NetDelegateDispatch.TryParseInvokeSignature(
            "System.Int32(System.Int32,System.Int32)", out var ret, out var ps), Is.True);
        Assert.That(ret, Is.EqualTo("System.Int32"));
        Assert.That(ps, Is.EqualTo(new[] { "System.Int32", "System.Int32" }).AsCollection);
    }

    [Test]
    public void TheInvokeSignatureParser_DoesNotSplitInsideGenericArguments()
    {
        // The one nesting that can occur in a rendered signature. Splitting naively on ',' would
        // report three parameters for a two-parameter delegate — and slot arity is exactly the
        // thing that must not be wrong (see the arity test below).
        Assert.That(NetDelegateDispatch.TryParseInvokeSignature(
            "System.Void(System.Collections.Generic.List<System.Int32,System.String>,System.Int32)",
            out _, out var ps), Is.True);
        Assert.That(ps.Count, Is.EqualTo(2));
        Assert.That(ps[0], Is.EqualTo("System.Collections.Generic.List<System.Int32,System.String>"));
    }

    [Test]
    public void SlotDescriptors_AreOneValueSlotPerParameter()
    {
        Assert.That(
            NetDelegateDispatch.CppSlotDescriptors("System.Int32(System.Int32,System.Int32)"),
            Is.EqualTo("{ {BLNET_SLOT_VALUE, 0}, {BLNET_SLOT_VALUE, 0} }"));
    }

    [Test]
    public void SlotDescriptorArity_MatchesTheInvokeSignature()
    {
        // ⛔ THE hazard this pins. P0's thunk deep-copies a QUEUED invocation by indexing
        // snapshot.slots[i] for i in [0, argc) where argc comes from the INVOKE, while `slots`
        // holds only the registration-time entries — and there is no bounds check. Registering
        // a different arity than the delegate is invoked with is an out-of-bounds read.
        Assert.That(NetDelegateDispatch.SlotCount("System.Void()"), Is.EqualTo(0));
        Assert.That(NetDelegateDispatch.SlotCount("System.Int32(System.Int32)"), Is.EqualTo(1));
        Assert.That(NetDelegateDispatch.SlotCount("System.Int32(System.Int32,System.Int32)"),
            Is.EqualTo(2));
    }

    [Test]
    public void AZeroParameterDelegate_EmitsNoSlotArray()
    {
        // blnet_register_callback guards this explicitly — `if (argc > 0) e.slots.assign(...)` —
        // so a zero-arg registration may legally pass nullptr.
        Assert.That(NetDelegateDispatch.CppSlotDescriptors("System.Void()"), Is.Null);
    }
}
