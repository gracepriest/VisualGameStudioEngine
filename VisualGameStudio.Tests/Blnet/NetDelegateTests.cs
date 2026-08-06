using System;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.Net;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
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

        // UNQUALIFIED. blnet_callback is a C typedef in blnet.h at GLOBAL scope, not a member of
        // the BasicLang::blnet namespace — qualifying it is C2039. This assertion originally
        // pinned the qualified spelling and passed, because an emission test can only confirm
        // the generator wrote what you told it to; the run-level proof is what caught it.
        Assert.That(proxies, Does.Match(@"[^:\w]blnet_callback\s"),
            "a delegate parameter's C++ spelling is an UNQUALIFIED callback handle");
        Assert.That(proxies, Does.Not.Contain("blnet::blnet_callback"),
            "qualifying it does not compile — blnet_callback is not in that namespace");

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

    // ------------------------------------------------------------------------------------
    // Step 5c — AddressOf lowering on the C++ backend (decision D-P12).
    //
    // Today MapUnaryOperator has no AddressOf arm and falls to `_ => "?"`, so `AddressOf Fn`
    // emits `t0 = ?Fn;` — invalid C++, silently, with no capability refusal. The result temp is
    // equally broken: AddressOf types as `Pointer To <return type>`, which MapType renders
    // verbatim as `Pointer To Integer t0 = {};`.
    //
    // D-P12: fuse the declaration into the assignment as `auto`, so C++ deduces the
    // function-pointer type. That works for Function and Sub alike without needing a parameter
    // list the IR does not carry.
    // ------------------------------------------------------------------------------------

    private static string CompileToCpp(string source)
    {
        var lexer = new Lexer(source);
        var parser = new Parser(lexer.Tokenize());
        var ast = parser.Parse();

        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            "fixture must be valid BasicLang: "
            + string.Join("; ", analyzer.Errors.Select(e => e.Message)));

        var ir = new IRBuilder(analyzer).Build(ast, "TestModule");
        return new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(ir);
    }

    // ⚠ SCOPE, measured rather than assumed. Two adjacent shapes are NOT reachable today, and
    // neither is caused by this change:
    //
    //   • `Apply(AddressOf Compare)` against a user `Delegate` parameter — the ANALYZER refuses
    //     it: "cannot convert from 'Pointer To Pointer To Integer' to 'Comparer'". Note the
    //     DOUBLE pointer; the AddressOf arm appears to wrap an already-pointer type. A separate
    //     pre-existing defect, upstream of codegen.
    //   • `Dim f = AddressOf Compare` — analyzes and reaches codegen, but the LOCALS pass
    //     declares `Pointer To Integer f`, which is not a C++ type. Chipped as task_4392b185.
    //
    // So these tests assert exactly what Step 5c owns: the operator mapping. The `auto` fusion
    // is exercised only once a delegate argument reaches lowering through the resolved .NET
    // path in Step 5d, which bypasses user-delegate type checking.

    [Test]
    public void AddressOfAFunction_NoLongerEmitsAQuestionMark()
    {
        var cpp = CompileToCpp(@"
Function Compare(a As Integer, b As Integer) As Integer
    Return a - b
End Function

Sub Main()
    Dim f = AddressOf Compare
End Sub");

        Assert.That(cpp, Does.Not.Contain("?Compare"),
            "MapUnaryOperator's `_ => \"?\"` default emitted a literal question mark INTO THE "
            + "GENERATED SOURCE — invalid C++, silently, with no capability refusal to catch it");
        Assert.That(cpp, Does.Contain("= Compare;"),
            "a method reference is just the function's name in C++");
    }

    // ------------------------------------------------------------------------------------
    // Step 5d — the MarshalNetArgument delegate arm.
    //
    // This is where every earlier step converges on one call: admissibility (1), the carried
    // invoke signature (2), the shared derivation (3), the callback wire row (4a), the managed
    // dispatcher (4b), the RAII guard (5a), the slot descriptors (5b) and the AddressOf/lambda
    // lowering (5c).
    //
    // List(Of Integer).Sort(Comparison(Of Integer)) is the plan's own mandatory shape and is
    // int(int,int) — all blittable scalars, so it clears 4b's v1 gate.
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// A purpose-built probe assembly with a SCALAR delegate member.
    ///
    /// <para>Nothing in the default surface will do. The ManagedOwned set is
    /// {Regex, Uri, Stream, FileInfo, DirectoryInfo}, and its only delegate-taking member is
    /// <c>Regex.Replace(String, MatchEvaluator)</c> — whose signature is <c>String(Match)</c>,
    /// which v1's scalar gate correctly refuses. And a locally constructed
    /// <c>List(Of Integer)</c> never crosses the boundary at all: BasicLang collections lower
    /// to <c>std::shared_ptr&lt;BasicLang::List&lt;T&gt;&gt;</c>, a NATIVE type, so
    /// <c>l.Sort(…)</c> is an ordinary native call that never reaches this lowering.</para>
    /// </summary>
    private const string DelegateProbeSource = @"
namespace DelegateProbe {
    public delegate int IntComparer(int a, int b);
    public static class Runner {
        public static int Run(IntComparer c) => c(1, 2);
    }
}";

    private static string LowerWithNet(string basicLang, string probeAssemblyPath)
    {
        var parser = new Parser(new Lexer(basicLang).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var resolver = NetTypeResolver.Create(
            NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { probeAssemblyPath }));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => resolver, nativeBackend: true);
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));
        Assert.That(analyzer.NetDiagnostics.Where(d => !d.IsWarning), Is.Empty,
            "net diagnostics:\n"
            + string.Join("\n", analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));

        var ir = new IRBuilder(analyzer).Build(ast, "TestModule");
        return new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(ir);
    }

    [Test]
    public void ALambdaArgument_RegistersACallbackGuardInTheCallPrologue()
    {
        using var dir = new ProbeDir();
        var probe = dir.EmitAssembly("BlnetDelegateProbe", DelegateProbeSource);

        var cpp = LowerWithNet(@"
Using DelegateProbe

Module M
 Sub Main()
  Console.WriteLine(Runner.Run(Function(a As Integer, b As Integer) a - b))
 End Sub
End Module", probe);

        Assert.That(cpp, Does.Contain("BasicLang::blnet::CallbackRef"),
            "§8.4 registration is an RAII guard in the PROLOGUE (D-P8) — never a paired "
            + "register/release, because WriteBack is skipped when NetCheckTyped throws and the "
            + "callback entry would leak permanently");
        Assert.That(cpp, Does.Contain("BLNET_SLOT_VALUE"),
            "the guard's constructor takes the BlnetSlotDesc[] computed in 5b");
        Assert.That(cpp, Does.Match(@"\w+\.get\(\)"),
            "the proxy parameter is a blnet_callback, so the guard's handle is what crosses");
    }

    [Test]
    public void AddressOfASub_NoLongerEmitsAQuestionMark()
    {
        // A Sub's symbol type is VoidType, so AddressOf types as `Pointer To Void`. Any filter
        // that tests the literal "void" silently lets that through, which is why the lowering
        // keys on node KIND rather than on the result type.
        var cpp = CompileToCpp(@"
Sub Handler()
    Console.WriteLine(""hi"")
End Sub

Sub Main()
    Dim h = AddressOf Handler
End Sub");

        Assert.That(cpp, Does.Not.Contain("?Handler"));
        Assert.That(cpp, Does.Contain("= Handler;"));
    }

    // ------------------------------------------------------------------------------------
    // Step 7 — THE RUN-LEVEL PROOF.
    //
    // Everything before this verifies emitted TEXT. A wrong unchecked cast, a wrong slot count
    // or a wrong-table read passes all of it. This is the first test that runs the generated
    // C++ and observes what it actually does.
    //
    // The mandatory shape is a RESULT-BEARING delegate, and that is not a stylistic preference:
    // at g_call_depth == 0 a result-bearing callback is REFUSED with BLNET_E_CROSS_THREAD_RESULT
    // while an Action is merely QUEUED and drained later — so an Action-only test passes even
    // with BlnetCallScope missing entirely. Only a value coming back proves inline dispatch.
    // ------------------------------------------------------------------------------------

    [Test]
    [Category("Integration")]
    [NonParallelizable]
    public void ARunningProgram_DispatchesAResultBearingDelegateInline()
    {
        using var dir = new ProbeDir();
        var probe = dir.EmitAssembly("BlnetDelegateProbe", DelegateProbeSource);
        var resolver = NetTypeResolver.Create(
            NetTypeResolverTestRefs.FrameworkPaths.Concat(new[] { probe }));

        const string source = @"
Using DelegateProbe

Module M
 Sub Main()
  Console.WriteLine(Runner.Run(Function(a As Integer, b As Integer) a - b))
 End Sub
End Module";

        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.ConfigureNetResolution(() => resolver, nativeBackend: true);
        Assert.That(analyzer.Analyze(ast), Is.True,
            "semantic errors:\n" + string.Join("\n", analyzer.Errors.Select(e => e.Message)));
        Assert.That(analyzer.NetDiagnostics, Is.Empty,
            "unexpected findings: " + string.Join(" | ",
                analyzer.NetDiagnostics.Select(d => d.Code + ": " + d.Message)));

        var module = new IRBuilder(analyzer).Build(ast, "TestModule");
        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(module);
        var surface = NetSurfaceCollector.Collect(
            new[] { module }, null, () => resolver, new List<NetReferenceDiagnostic>());
        Assert.That(surface.IsNonEmpty, Is.True, "the program must draw a surface");

        // The slot name is RE-DERIVED from the seams production uses, so a mangling or
        // overload-selection drift breaks the C++ compile rather than silently passing.
        // NetStubHarness.Winner cannot serve here — it resolves against the framework-only
        // SharedResolver, and this member lives in the probe assembly.
        var run = resolver.ResolveOverload(
            "DelegateProbe.Runner", NetCallForm.Static, "Run",
            new[] { NetTypeResolver.LambdaArgumentSpelling(2) });
        Assert.That(run.Outcome, Is.EqualTo(NetOverloadOutcome.Resolved),
            "fixture provenance: Runner.Run must resolve against a 2-parameter lambda — this is "
            + "also an independent check that D-P11's target-typing selects the delegate overload");
        var slot = NetNameMangler.Mangle(run.Member!);

        // The stub stands in for the managed side: it receives the callback handle and invokes
        // it through P0's universal thunk with (7, 3). Because the REAL proxy opens a
        // BlnetCallScope around this call, g_call_depth > 0 and the thunk must dispatch INLINE.
        var stub = NetStubHarness.StubTranslationUnit(new[]
        {
            new NetStubHarness.StubSlot(slot,
                @"[](uint64_t cb, int32_t* result) -> int32_t {
                    uint64_t args[2] = { 7, 3 };
                    uint64_t r = 0;
                    int32_t st = BasicLang::blnet::blnet_invoke_callback(cb, args, 2, &r);
                    if (st != BLNET_OK) { std::printf(""THUNK-FAILED %d\n"", st); return st; }
                    *result = (int32_t)r;
                    return BLNET_OK;
                }"),
        });

        var stdout = NetStubHarness.RunWithStub(cpp, surface, stub);

        Assert.That(stdout, Does.Not.Contain("THUNK-FAILED"),
            "a non-OK status here means the callback never ran inline — BLNET_E_CROSS_THREAD_RESULT "
            + "(5) specifically means g_call_depth was 0, i.e. the proxy's BlnetCallScope is missing");
        Assert.That(stdout.Trim(), Is.EqualTo("4"),
            "7 - 3 = 4 proves the WHOLE path: the slots unpacked in the right ORDER (3 - 7 would "
            + "give -4), the BasicLang lambda actually ran, and its value came back through "
            + "*result. This is the first test in Task 11 that could catch a bad cast or a wrong "
            + "slot count.");
    }

    /// <summary>
    /// A scratch directory that compiles a C# probe assembly for the fixture. Mirrors
    /// <c>NetCallLoweringTests.TempDir</c>, which is private to that fixture — copied rather
    /// than shared because it is three lines of test scaffolding, and lifting it into a shared
    /// helper would give two fixtures a coupling neither needs.
    /// </summary>
    private sealed class ProbeDir : IDisposable
    {
        private readonly string _path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "blnet-delegate-" + Guid.NewGuid().ToString("N"));

        public ProbeDir() => System.IO.Directory.CreateDirectory(_path);

        public string EmitAssembly(string name, string source)
        {
            var path = System.IO.Path.Combine(_path, name + ".dll");
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                name,
                new[] { Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source) },
                NetTypeResolverTestRefs.FrameworkPaths.Select(
                    p => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(p)),
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                    Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

            Microsoft.CodeAnalysis.Emit.EmitResult emit;
            using (var stream = System.IO.File.Create(path))
                emit = compilation.Emit(stream);

            Assert.That(emit.Success, Is.True,
                "fixture probe assembly failed to build: " + string.Join("\n",
                    emit.Diagnostics.Where(d =>
                        d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)));
            return path;
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(_path, recursive: true); }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Test]
    public void AZeroParameterDelegate_EmitsNoSlotArray()
    {
        // blnet_register_callback guards this explicitly — `if (argc > 0) e.slots.assign(...)` —
        // so a zero-arg registration may legally pass nullptr.
        Assert.That(NetDelegateDispatch.CppSlotDescriptors("System.Void()"), Is.Null);
    }
}
