using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using static VisualGameStudio.Tests.Msil.MsilHarness;

namespace VisualGameStudio.Tests.Msil;

/// <summary>
/// ADR-0016 D3, the MSIL contract: for a constructor whose base-args prologue creates a lambda,
/// <c>ClosureLowering</c> allocates the environment and hoists the captured parameters BEFORE the
/// prologue, and stores <c>Me</c> into the environment <b>immediately after</b>
/// <c>IRBaseConstructorCall</c> — "before that point <c>ldarg.0</c> appears only as the receiver of the
/// base <c>call</c>".
///
/// <para>⛔ <b>Nothing but the IL text can see this.</b> Mutant M3 — <c>Me</c> stored into the
/// environment BEFORE the base call — RUNS CORRECTLY on the CLR: the store of an uninitialised
/// <c>this</c> is unverifiable IL, not a fault, so every run-time test in this suite passes with it
/// (measured, ADR-0016 "Detectable only by ILVerify"). ILVerify reports <c>UninitStack</c>; the suite has
/// no ILVerify (checked: no reference, no package, no harness), so these tests read the emitted
/// <c>.il</c> instead, and a control test proves the reader is not vacuous by handing it the M3 shape.
/// D4 (BC31095/6) is what makes the ordering sound — a base-args lambda cannot mention <c>Me</c> — but
/// the ORDER is what keeps the environment's <c>__me</c> from holding an object whose constructor has
/// not run.</para>
///
/// <para>E14 (a base-args lambda that writes <c>p</c>, and a BODY lambda that reads <c>A</c>/<c>p</c>,
/// i.e. <c>Me</c>) and E15 (a read-only base-args lambda, a body lambda) — the ADR-0010 D6 regression
/// check: "a body lambda created after the base call still sees a non-null <c>Me</c>".</para>
/// </summary>
[TestFixture]
public class MsilBaseConstructorOrderingTests
{
    internal const string E14_BodyLambdaUsesMe = """
        Class Base
            Public F As Action
            Public Sub New(f As Action)
                Me.F = f
            End Sub
        End Class

        Class D
            Inherits Base
            Public A As Integer
            Public G As Func(Of Integer)
            Public Sub New(p As Integer)
                MyBase.New(Sub() p = p + 1)
                A = 5
                G = Function() A + p
            End Sub
        End Class

        Sub Main()
            Dim d As New D(1)
            d.F()
            d.A = d.A + 10
            Console.WriteLine(d.G())
        End Sub
        """;

    internal const string E15_ReadOnlyLambdaWithMeBody = """
        Class Base
            Public G As Func(Of Integer)
            Public Sub New(g As Func(Of Integer))
                Me.G = g
            End Sub
        End Class

        Class D
            Inherits Base
            Public A As Integer
            Public H As Func(Of Integer)
            Public Sub New(p As Integer, q As Integer)
                MyBase.New(Function() p * 10)
                A = q
                H = Function() A + p
            End Sub
        End Class

        Sub Main()
            Dim d As New D(2, 3)
            Console.WriteLine(d.G() & " " & d.H())
        End Sub
        """;

    // ============================================================================================
    // The IL reader
    // ============================================================================================

    /// <summary>The instruction lines of class <c>D</c>'s <c>.ctor</c>, trimmed, in order.</summary>
    private static List<string> ConstructorOfD(string il)
    {
        var classStart = il.IndexOf(".class public auto ansi beforefieldinit 'D'", StringComparison.Ordinal);
        Assert.That(classStart, Is.GreaterThanOrEqualTo(0), "class D not found in the IL");
        var ctorStart = il.IndexOf(".ctor(", classStart, StringComparison.Ordinal);
        Assert.That(ctorStart, Is.GreaterThanOrEqualTo(0), "D's .ctor not found in the IL");
        var open = il.IndexOf('{', ctorStart);
        var end = il.IndexOf("// end of method .ctor", open, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(open), "the end of D's .ctor was not found");
        return il.Substring(open, end - open).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
    }

    private static readonly Regex BaseCall = new(@"^call instance void '?Base'?::\.ctor\(", RegexOptions.Compiled);
    private static readonly Regex MeStore = new(@"^stfld class '?D'? '?D'?/'<>c__Env\d+'::'__me'$", RegexOptions.Compiled);
    private static readonly Regex EnvAlloc = new(@"^newobj instance void '?D'?/'<>c__Env\d+'::\.ctor\(\)$", RegexOptions.Compiled);

    /// <summary>Everything D3's MSIL contract forbids, as text. Empty means the order is right.</summary>
    internal static List<string> OrderViolations(List<string> ctor)
    {
        var problems = new List<string>();
        var call = ctor.FindIndex(l => BaseCall.IsMatch(l));
        if (call < 0) { problems.Add("D's .ctor has no `call instance void Base::.ctor(...)`"); return problems; }

        var meStores = ctor.Select((l, i) => (l, i)).Where(x => MeStore.IsMatch(x.l)).Select(x => x.i).ToList();
        if (meStores.Count == 0) problems.Add("no `stfld ... '__me'` — the body lambda's environment never receives Me");
        foreach (var store in meStores)
            if (store < call) problems.Add($"`Me` is stored into the environment at line {store}, BEFORE the base call (line {call}): "
                                           + "unverifiable IL (ILVerify UninitStack), and it runs anyway");
        if (meStores.Count > 0 && meStores.Min() > call + 3)
            problems.Add($"`Me` is stored at line {meStores.Min()}, not IMMEDIATELY after the base call (line {call}) — "
                         + "D3: the Me store is emitted immediately after IRBaseConstructorCall");

        var allocs = ctor.Select((l, i) => (l, i)).Where(x => EnvAlloc.IsMatch(x.l)).Select(x => x.i).ToList();
        if (allocs.Count == 0) problems.Add("no environment allocation in the constructor");
        else if (allocs.Min() > call) problems.Add("the environment is allocated AFTER the base call — D3 puts the allocation and the parameter hoists BEFORE the prologue");

        var receivers = ctor.Take(call).Count(l => l == "ldarg.0");
        if (receivers != 1) problems.Add($"`ldarg.0` appears {receivers} times before the base call — only ONE (the base call's own receiver) is allowed");
        return problems;
    }

    // ============================================================================================
    // Tests — standard and aggressive IL
    // ============================================================================================

    private static IEnumerable<TestCaseData> Programs()
    {
        yield return new TestCaseData(E14_BodyLambdaUsesMe, false).SetName("E14_standard");
        yield return new TestCaseData(E14_BodyLambdaUsesMe, true).SetName("E14_aggressive");
        yield return new TestCaseData(E15_ReadOnlyLambdaWithMeBody, false).SetName("E15_standard");
        yield return new TestCaseData(E15_ReadOnlyLambdaWithMeBody, true).SetName("E15_aggressive");
    }

    /// <summary>
    /// ⭐ THE ORDER: the environment is allocated and the captured parameter hoisted first; the base
    /// call runs with `ldarg.0` used ONLY as its receiver; and the `stfld ... '__me'` into the
    /// environment comes AFTER the `call instance void Base::.ctor` — kills mutant M3.
    /// </summary>
    [TestCaseSource(nameof(Programs))]
    public void MeIsStoredIntoTheEnvironment_AfterTheBaseConstructorCall(string source, bool aggressive)
    {
        var ctor = ConstructorOfD(CompileToIl(source, aggressive: aggressive));

        Assert.That(OrderViolations(ctor), Is.Empty, string.Join("\n", ctor));

        var call = ctor.FindIndex(l => BaseCall.IsMatch(l));
        var store = ctor.FindIndex(l => MeStore.IsMatch(l));
        Assert.That(store, Is.GreaterThan(call), "stfld '__me' must follow `call ... Base::.ctor`");
    }

    /// <summary>
    /// The control: the reader must be able to FAIL. Hand it the M3 shape — the same E14 constructor with
    /// the `Me` store moved in front of the base call — and it reports it; a reader that returned "clean"
    /// for everything would let mutant M3 through, which is exactly why the run-time tests could not.
    /// </summary>
    [Test]
    public void TheReader_RejectsMutantM3sShape()
    {
        var ctor = ConstructorOfD(CompileToIl(E14_BodyLambdaUsesMe));
        var call = ctor.FindIndex(l => BaseCall.IsMatch(l));
        var store = ctor.FindIndex(l => MeStore.IsMatch(l));
        // M3: [ldloc env; ldarg.0; stfld __me] hoisted to before the prologue's base call.
        var mutated = new List<string>(ctor);
        var block = mutated.GetRange(store - 2, 3);
        mutated.RemoveRange(store - 2, 3);
        mutated.InsertRange(call - 3, block);

        var problems = OrderViolations(mutated);
        Assert.That(problems, Is.Not.Empty, string.Join("\n", mutated));
        Assert.That(problems, Has.Some.Contains("BEFORE the base call"));
    }

    /// <summary>The environment holds the captured PARAMETER by the time the prologue's lambda exists:
    /// the hoist (`stfld ... '<>c__Env0'::'p'`) precedes the delegate's `ldftn`, which precedes the
    /// base call — so the base-args lambda reads the parameter, not a stale slot.</summary>
    [Test]
    public void TheCapturedParameterIsHoistedIntoTheEnvironmentBeforeTheBaseArgsLambdaIsBuilt()
    {
        var ctor = ConstructorOfD(CompileToIl(E14_BodyLambdaUsesMe));
        var hoist = ctor.FindIndex(l => l.StartsWith("stfld int32 'D'/'<>c__Env", StringComparison.Ordinal) && l.EndsWith("::'p'", StringComparison.Ordinal));
        var firstLambda = ctor.FindIndex(l => l.StartsWith("ldftn", StringComparison.Ordinal));
        var call = ctor.FindIndex(l => BaseCall.IsMatch(l));

        Assert.That(hoist, Is.GreaterThanOrEqualTo(0), string.Join("\n", ctor));
        Assert.That(hoist, Is.LessThan(firstLambda), "the parameter is hoisted before the delegate is created");
        Assert.That(firstLambda, Is.LessThan(call), "the delegate is created before the base call consumes it");
    }
}
