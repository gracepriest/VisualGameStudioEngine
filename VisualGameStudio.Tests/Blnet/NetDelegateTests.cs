using System;
using System.Linq;
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
}
