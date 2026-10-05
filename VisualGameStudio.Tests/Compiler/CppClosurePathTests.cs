using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

// =====================================================================================
//  #140 / ADR-0019 — which representation each C++ closure root takes. Every test here needs
//  only CppCodeGenerator.Generate / GenerateSplit and the ClosurePaths test seam (ruling D5.4),
//  never a C++ compiler, so the fixture runs in the fast subset. The programs are run (with VB's
//  output) by CppClosureRunTests.
//
//  The ruling's path rule, per ROOT (the outermost non-lambda function and every lambda it creates):
//    (1) ClosureLowering accepts it                      -> Lowered
//    (2) it refuses AND W2 (CheckLambdaCaptureWrites)
//        holds for every lambda of the root              -> ByCopy, byte-identical to master's `[=]`
//    (3) it refuses AND W2 does not hold                 -> CppCapabilityException: W2's text first,
//                                                           then "closure lowering cannot lower '<root>'
//                                                           either (#140): C++: <reason>"
// =====================================================================================

[TestFixture]
public class CppClosurePathTests
{
    private static readonly CppEntry[] AllEntries =
        { CppEntry.Plain, CppEntry.Standard, CppEntry.Aggressive, CppEntry.Project, CppEntry.Split };

    // ---- (2) The by-copy fallback set, pinned BY NAME (ruling D3: it may only shrink) ----------------------

    private static IEnumerable<TestCaseData> FallbackSet() =>
        CppClosurePrograms.Fallback().Select(p => new TestCaseData(p).SetName("Fallback_" + p.Name.Replace('/', '_')));

    /// <summary>
    /// ⭐ Each program of the fallback set: (b) <c>ClosurePaths</c> says <c>ByCopy</c> for its root — in every
    /// entry point, the single-file ones and the project's <c>CompileProjectFiles</c> → <c>Generate</c> and
    /// <c>GenerateSplit</c> — and (c) the emitted C++ has NO environment for it: no environment class, no
    /// holder struct, and the lambdas are the <c>[=]</c> ones master emits. (a), that it prints VB's output, is
    /// <c>CppClosureRunTests.TheFallbackSet_RunsWithVbsOutput</c>.
    /// </summary>
    [TestCaseSource(nameof(FallbackSet))]
    public void TheFallbackSet_TakesTheByCopyPath_WithNoEnvironment_AndKeepsTheByCopyLambdas(CppClosureProgram program)
    {
        Assert.Multiple(() =>
        {
            foreach (var entry in AllEntries)
            {
                var build = CppClosures.Compile(program.Source, entry);
                Assert.That(build.PathOf(program.Root), Is.EqualTo(CppClosurePath.ByCopy), $"{program.Name}: {entry}");
                Assert.That(build.Paths.Where(p => p.Root != program.Root), Is.Empty,
                    $"{program.Name}: {entry}: the program has exactly one root that creates a lambda: {build.Describe()}");

                var text = build.AllText;
                // (boolean form: a failing Does.Contain would print the whole translation unit, runtime and all)
                Assert.That(text.Contains("c__Env"), Is.False, $"{program.Name}: {entry}: a by-copy root has no environment class");
                Assert.That(text.Contains("BasicLangClosures"), Is.False, $"{program.Name}: {entry}: ... and no holder struct");
                // (scope: none of these programs takes AddressOf of a class method — one that does DOES emit the thunk,
                //  see TheByCopyFallback_ForwardsAnAddressOfOfAClassMethod_ThroughTheThunk_AndKeepsAModuleProcedureAFunctionPointer)
                Assert.That(text.Contains("blTarget"), Is.False, $"{program.Name}: {entry}: ... and no forwarding thunk");
                Assert.That(text.Contains("[="), Is.True, $"{program.Name}: {entry}: the lambdas keep the by-copy capture list ([=] or [=, ex = …])");
            }
        });
    }

    private const string ByCopyAddressOfAClassMethod = """
        Delegate Sub Notify(msg As String)

        Class Greeter
            Public Prefix As String
            Public Sub Greet(m As String)
                Console.WriteLine(Prefix & m)
            End Sub
        End Class

        Sub Main()
            Dim lim As Integer = 5
            Dim probe As Func(Of Integer) = Function() lim + 1
            Dim v As Integer = probe()
            Select Case v
                Case Is > 0 When v > lim + 1
                    Console.WriteLine("big")
                Case Else
                    Console.WriteLine("small")
            End Select
            Dim g As New Greeter()
            g.Prefix = "hi "
            Dim d As Notify = AddressOf g.Greet
            d("bob")
        End Sub
        """;

    private const string ByCopyAddressOfAModuleProcedure = """
        Delegate Function Transform(n As Integer) As Integer

        Function Pick(up As Boolean) As Transform
            Dim lim As Integer = 5
            Dim probe As Func(Of Integer) = Function() lim + 1
            Dim v As Integer = probe()
            Select Case v
                Case Is > 0 When v > lim + 1
                    Console.WriteLine("big")
                Case Else
                    Console.WriteLine("small")
            End Select
            If up Then Return AddressOf Inc
            Return Function(n) n - 1
        End Function

        Function Inc(n As Integer) As Integer
            Return n + 1
        End Function

        Sub Main()
            Console.WriteLine(Pick(True)(10) + Pick(False)(10))
        End Sub
        """;

    /// <summary>
    /// ⭐ #201 — the scope of <see cref="TheFallbackSet_TakesTheByCopyPath_WithNoEnvironment_AndKeepsTheByCopyLambdas"/>'s
    /// "no forwarding thunk" check. It holds there only because none of those programs takes <c>AddressOf</c> of a class
    /// method. A by-copy root that does DOES emit the lowered path's forwarding closure (<c>[blTarget = g](…) { … }</c>,
    /// the receiver held by copy) — and still has no environment class and no holder; one that takes <c>AddressOf</c> of a
    /// MODULE procedure keeps the plain function pointer, declared as <c>decltype(&amp;Inc)</c>, with no thunk at all.
    /// </summary>
    [Test]
    public void TheByCopyFallback_ForwardsAnAddressOfOfAClassMethod_ThroughTheThunk_AndKeepsAModuleProcedureAFunctionPointer()
    {
        Assert.Multiple(() =>
        {
            foreach (var entry in AllEntries)
            {
                var method = CppClosures.Compile(ByCopyAddressOfAClassMethod, entry);
                Assert.That(method.PathOf("Main"), Is.EqualTo(CppClosurePath.ByCopy), $"class method: {entry}");
                var text = method.AllText;
                Assert.That(text.Contains("[blTarget = "), Is.True, $"class method: {entry}: the receiver is held by copy in the forwarding closure");
                Assert.That(text.Contains("blTarget->Greet("), Is.True, $"class method: {entry}: and the method is called through it");
                Assert.That(text.Contains("c__Env"), Is.False, $"class method: {entry}: ... yet there is still no environment class");
                Assert.That(text.Contains("BasicLangClosures"), Is.False, $"class method: {entry}: ... and no holder struct");

                var module = CppClosures.Compile(ByCopyAddressOfAModuleProcedure, entry);
                Assert.That(module.PathOf("Pick"), Is.EqualTo(CppClosurePath.ByCopy), $"module procedure: {entry}");
                Assert.That(module.AllText.Contains("blTarget"), Is.False, $"module procedure: {entry}: a function pointer needs no thunk");
                Assert.That(module.AllText.Contains("decltype(&Inc)"), Is.True, $"module procedure: {entry}: its temp is declared with the others");
            }
        });
    }

    /// <summary>
    /// ⭐ THE GROWTH DETECTOR (ruling D3: "the census fallback set is pinned BY NAME and may only shrink; growth
    /// fails"). Every program of every fixture this ticket touches or adds — each <c>const string</c> program
    /// of the fixtures listed in <see cref="CorpusTypes"/>, found by reflection, so a program added to one of
    /// them is swept without anyone remembering to list it — is generated through the standard pipeline, and
    /// the roots that land on <c>ByCopy</c> must be EXACTLY this set. A lowering regression that pushes a root
    /// onto <c>[=]</c> is invisible whenever W2 happens to accept it (right output today, the L13 clang-failure
    /// class back tomorrow); this is where it fails. When a D9 shape is lifted in the lowering, delete its row
    /// here in the same commit (ruling D3: "its fallback pins flip to lowered pins").
    /// </summary>
    [Test]
    public void NoOtherProgramInTheCorpus_LandsOnTheByCopyPath()
    {
        var byCopy = new SortedSet<string>(StringComparer.Ordinal);
        var swept = 0;
        foreach (var (name, source) in CorpusPrograms())
        {
            var build = CppClosures.TryCompile(source);
            if (build == null) continue;   // a fragment, a front-end rejection, or a both-refused program: no path
            swept++;
            foreach (var p in build.Paths.Where(p => p.Path == CppClosurePath.ByCopy)) byCopy.Add($"{name} [{p.Root}]");
        }

        Assert.That(swept, Is.GreaterThan(150), "the sweep must actually find the corpus (reflection over the fixtures)");
        Assert.That(byCopy, Is.EquivalentTo(ExpectedByCopy),
            "The by-copy fallback set changed. A NEW entry means a root the lowering used to lower is skipped now " +
            "(the lowering regressed and `[=]` silently caught it — ruling D3's revisit-if). A MISSING entry means a " +
            "D9 shape was lifted: delete it from ExpectedByCopy and flip its pin to a lowered pin.");
    }

    /// <summary>Every root of the corpus that is NOT lowered, by program (<c>Type.Field [root]</c>).</summary>
    private static readonly string[] ExpectedByCopy =
    {
        // the ten fallback programs that run (ruling D1) — Fallback()
        "CppClosurePrograms.Iterator [Gen]",
        "CppClosurePrograms.IteratorReadOnly [Gen]",
        "CppClosurePrograms.MyBaseInLambda [Derived.Tag]",
        "CppClosurePrograms.GenericClassIntCapture [Holder.CountTo]",
        "CppClosurePrograms.WhenGuardReadOnly [Main]",
        "CppClosurePrograms.N9LaterSibling [Main]",
        "CppClosurePrograms.TwoCatchTypes [Main]",
        "CppClosurePrograms.X1 [Main]",
        "CppClosurePrograms.X3 [Main]",
        "PerIterationLoopBodyDimProbes.E20 [Main]",
        // fallback roots that also fail the C++ compiler, for a gap that is not the lambda's
        "CppClosurePrograms.Async [Work]",
        "CppClosurePrograms.AsyncReadOnly [Work]",
        "CppClosurePrograms.GenericCapture [Wrap]",
        // (D09, CppClosurePrograms.ModuleInitializer [__lambda_0], is ByCopy too but cannot appear in this sweep: it is a
        //  property, not a const — the verifier is in Throw mode in the suite and fires on it; see
        //  TheModuleInitializerLambda_IsTheOnlyPreExistingVerifierFire)
        // the falsifiers of D5 / atomicity that are fallback by construction
        "CppClosurePrograms.RI3 [Main]",
        "CppClosurePrograms.CX3 [Main]",
        "ClosureLoweringContractPrograms.AT1 [Derived.Tag]",
        "ClosureLoweringContractPrograms.TwoRoots [Refused]",
        // fixtures of the moved pins that already carried a fallback program, and the three fallback variants of
        // E5, E8 and E9e (a read-only When guard forces the by-copy path). E8 and E9e RUN there since #201 (the
        // roots still take this path); E5_OnTheFallback pins what remains
        "NothingStringTextExecutionTests.E12 [Main]",
        "NothingStringTextExecutionTests.E5_OnTheFallback [Main]",
        "UserDelegateConversionExecutionTests.E8_OnTheFallback [Main]",
        "UserDelegateConversionExecutionTests.E9e_OnTheFallback [Pick]",
    };

    private static readonly Type[] CorpusTypes =
    {
        typeof(CppClosurePrograms), typeof(ClosureLoweringContractPrograms), typeof(ClosureLoweringProbes),
        typeof(PerIterationLoopBodyDimProbes), typeof(MultiLineFunctionLambdaProbes),
        typeof(BaseConstructorCallLoweringExecutionTests), typeof(BaseConstructorCallCppRefusalTests),
        typeof(DelegateMemberInvocationExecutionTests), typeof(LicmKillVocabularyShapes),
        typeof(CopyPropagationSharedVocabularyProbes), typeof(IsIsNotOperatorExecutionTests),
        typeof(UserDelegateConversionExecutionTests), typeof(NothingStringTextExecutionTests),
        typeof(MsilBaseConstructorOrderingTests),
    };

    /// <summary>(Type.Field, source) for every <c>const string</c> of <see cref="CorpusTypes"/> that looks like a program.</summary>
    private static IEnumerable<(string Name, string Source)> CorpusPrograms()
    {
        foreach (var type in CorpusTypes)
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string)) continue;
                var source = (string)field.GetRawConstantValue();
                if (source != null && (source.Contains("End Sub") || source.Contains("End Function")))
                    yield return ($"{type.Name}.{field.Name}", source);
            }
    }

    // ---- (3) The both-refused set (ruling D1 case 3) ----------------------------------------------------------

    /// <summary>(source, root, the lowering's reason, the variable W2 names).</summary>
    private static IEnumerable<TestCaseData> BothRefused()
    {
        yield return new TestCaseData(CppClosurePrograms.D07, "Main", "a Select Case 'When' guard in 'Main'", "lim").SetName("BothRefused_D07_R15_whenGuardOverAWrittenCapture");
        yield return new TestCaseData(CppClosurePrograms.GenericMethodCaptureWrite, "Pick", "typed by the generic parameter 'T'", "cur").SetName("BothRefused_D14_genericMethodCaptureWrite");
        yield return new TestCaseData(BaseConstructorCallCppRefusalTests.R12_ByRefArg, "Main", "passed ByRef to 'Bump'", "n").SetName("BothRefused_M07_R12_byRefArgument");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.E09_GenericDerived, "GBox.New", "inside the generic class 'GBox'", "p").SetName("BothRefused_E09_genericDerivedClass");
        yield return new TestCaseData(CppClosurePrograms.ByRefFromALambda, "Main", "passed ByRef to 'Inc' in '__lambda_0'", "n").SetName("BothRefused_K13_byRefFromALambda");
        // root identity (D5.1): the two lambdas of ONE root, whichever of them carries the refusal
        yield return new TestCaseData(CppClosurePrograms.RI1, "Main", "a Select Case 'When' guard in '__lambda_0'", "n").SetName("BothRefused_RI1_outerWritesInnerRefused");
        yield return new TestCaseData(CppClosurePrograms.RI2, "Main", "a Select Case 'When' guard in '__lambda_0'", "n").SetName("BothRefused_RI2_innerWritesOuterRefused");
    }

    /// <summary>
    /// ⭐ A program both paths refuse is refused with W2's text FIRST — naming the captured variable and its
    /// creator, with #140 — then "closure lowering cannot lower '&lt;root&gt;' either (#140): C++: …" and the
    /// lowering's own reason, in every in-process entry point; nothing is returned (no C++ text exists to
    /// write). The refusal is a <c>CppCapabilityException</c>, the channel <c>CppProjectBuilder</c> reports as
    /// BL6001. RI1 and RI2 are ruling D5.1's falsifier: if either ran, the OTHER lambda's refusal let a write
    /// slip onto <c>[=]</c> — W2 and the lowering would disagree about what a root is.
    /// </summary>
    [TestCaseSource(nameof(BothRefused))]
    public void BothRefused_W2First_ThenTheLoweringsReason_InEveryEntryPoint(string source, string root, string loweringReason, string variable)
        => CppClosures.AssertBothRefused(source, root, loweringReason, variable);

    /// <summary>
    /// The second reason names only the roots that are actually both-refused: a program with a both-refused root
    /// (D07's <c>Main</c>) and a SOUND by-copy root (an iterator, which W2 admits) is refused for <c>Main</c> alone
    /// — the message must not tell the user that <c>Gen</c> cannot be lowered "either", because W2 holds for it and
    /// it would have run (the rule is per root, ruling D1).
    /// </summary>
    [Test]
    public void TheRefusalNamesOnlyTheRootsBothPathsRefuse_NotEveryRootTheLoweringSkipped()
    {
        const string source = """
            Iterator Function Gen() As IEnumerable(Of Integer)
                Dim k As Integer = 1
                Dim f = Function() k + 1
                Yield f()
            End Function
            Sub Main()
                Dim lim As Integer = 5
                Dim g = Sub() lim = lim + 1
                g()
                Dim v As Integer = 7
                Select Case v
                    Case Is > 0 When v > lim + 1
                        Console.WriteLine("big")
                    Case Else
                        Console.WriteLine("small")
                End Select
                For Each x As Integer In Gen()
                    Console.WriteLine(x)
                Next
            End Sub
            """;
        Assert.Multiple(() =>
        {
            foreach (var entry in AllEntries)
            {
                var message = CppClosures.Refusal(source, entry).Message;
                Assert.That(message.Contains("closure lowering cannot lower 'Main' either (#140): C++: "), Is.True, $"{entry}\n{message}");
                Assert.That(message.Contains("'Gen'"), Is.False, $"{entry}: Gen is a sound by-copy root and must not be named\n{message}");
            }
        });
    }

    /// <summary>
    /// Root identity is ONE definition (ruling D5.1): W2 reads <c>ClosureLowering.CreatorsOf</c>, the walk the lowering
    /// itself uses over EVERY function of the module — class members, accessors and interface default bodies included
    /// — not a scan of <c>module.Functions</c> of its own (master's W2 used <c>OptimizationPass.LambdaReferences</c> there,
    /// and a creator outside <c>module.Functions</c> was invisible to it, so its lambdas escaped the by-copy rule
    /// unjudged). No source program reaches that difference — the parser never gives an interface method a body — so
    /// the module is built by hand: D07's helper is moved out of <c>module.Functions</c> and registered as an interface
    /// method's default implementation. Both paths must still refuse it, naming the helper.
    /// </summary>
    [Test]
    public void RootIdentity_IsTheLoweringsOwnDefinition_EvenForACreatorOutsideModuleFunctions()
    {
        const string source = """
            Sub Helper()
                Dim lim As Integer = 5
                Dim f = Sub() lim = lim + 1
                f()
                Dim v As Integer = 7
                Select Case v
                    Case Is > 0 When v > lim + 1
                        Console.WriteLine("big")
                    Case Else
                        Console.WriteLine("small")
                End Select
            End Sub
            Sub Main()
                Helper()
            End Sub
            """;
        var module = CppClosures.Optimized(source, CppEntry.Standard);
        var helper = module.Functions.First(f => f.Name == "Helper");
        module.Functions.Remove(helper);
        var iface = new IRInterface("IHelp");
        iface.Methods.Add(new IRInterfaceMethod { Name = "Helper", HasDefaultImplementation = true, DefaultImplementation = helper });
        module.Interfaces["IHelp"] = iface;

        Assert.That(IRTempNames.AllFunctions(module), Does.Not.Contain(helper), "precondition: nothing but the interface holds the creator");
        var ex = Assert.Throws<CppCapabilityException>(
            () => new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(module));
        CppClosures.AssertBothRefusedMessage(ex!.Message, "Helper", "a Select Case 'When' guard in 'Helper'", "lim", "hand-built IR");
    }

    // ---- (1) Lowered rows ------------------------------------------------------------------------------------

    private static IEnumerable<TestCaseData> LoweredRows()
    {
        // H′ over H: the .NET delegate facade's arity cap is MSIL's, not a lowering fact (ruling D1)
        yield return new TestCaseData(ClosureLoweringContractPrograms.D10, "Main").SetName("Lowered_D10_func10_noArityCapOnCpp");
        yield return new TestCaseData(ClosureLoweringContractPrograms.D11, "Main").SetName("Lowered_D11_action9_noArityCapOnCpp");
        yield return new TestCaseData(CppClosurePrograms.AR1, "Main").SetName("Lowered_AR1_func10_withACapturedWrite");
        yield return new TestCaseData(CppClosurePrograms.M01_L5, "Main").SetName("Lowered_M01_L5");
        yield return new TestCaseData(CppClosurePrograms.EX1, "Main").SetName("Lowered_EX1_exitForInATryFinally");
        yield return new TestCaseData(CppClosurePrograms.EX2, "Find").SetName("Lowered_EX2_returnFromTheLoopBody");
        yield return new TestCaseData(CppClosurePrograms.EX3, "Main").SetName("Lowered_EX3_forEachExitForThenTheTail");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E06, "Main").SetName("Lowered_E06_exitForOutOfACountedBody");
        yield return new TestCaseData(CppClosurePrograms.CX1, "Main").SetName("Lowered_CX1_catchVariableReadAfterTheCatch");
        yield return new TestCaseData(CppClosurePrograms.CX2, "Main").SetName("Lowered_CX2_throwTheCapturedVariable");
        yield return new TestCaseData(CppClosurePrograms.CX2b, "Main").SetName("Lowered_CX2b_throwTheCapturedVariableThroughATwoClauseLadder");
        yield return new TestCaseData(CppClosurePrograms.NM1, "Main").SetName("Lowered_NM1_userNamesLikeMangledOnes");
        yield return new TestCaseData(CppClosurePrograms.GenericLambdaParameter, "Apply").SetName("Lowered_D16_lambdaParameterTypedByAGenericMethod");
        yield return new TestCaseData(CppClosurePrograms.ListForEach, "Main").SetName("Lowered_L6_listForEach");
        // programs W2 REFUSED before #140: a write to a captured variable is exactly what lowering is for
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.B1, "D.New").SetName("Lowered_B1_lambdaWritesItsCapturedParameter");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E03, "Main").SetName("Lowered_E03_finallyWritesTheCapture");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.L7, "Main").SetName("Lowered_L7_lambdaWritesThePreviousIterationVariable");
    }

    /// <summary>
    /// A root the lowering accepts takes the lowered path in EVERY entry point — and W2 is never consulted for it
    /// (ruling D3: "W2 is never evaluated for a lowered root"). B1, E03 and L7 are programs W2 refuses by name
    /// (the lambda writes what it captures), so a backend that evaluated W2 for lowered roots too would throw
    /// here: this is the fast killer of that mutation. D10, D11 and AR1 are the arity rows: lowered because
    /// C++'s options carry no cap, where MSIL's 8 / 9 refuse them.
    /// </summary>
    [TestCaseSource(nameof(LoweredRows))]
    public void ALoweredRoot_TakesTheLoweredPath_InEveryEntryPoint_AndW2IsNeverConsulted(string source, string root)
    {
        Assert.Multiple(() =>
        {
            foreach (var entry in AllEntries)
            {
                var build = CppClosures.Compile(source, entry);
                Assert.That(build.PathOf(root), Is.EqualTo(CppClosurePath.Lowered), $"root '{root}', {entry}");
                Assert.That(build.RootsOn(CppClosurePath.ByCopy), Is.Empty, $"{entry}: {build.Describe()}");
            }
        });
    }

    /// <summary>One program, two roots: <c>Lowerable</c> is lowered, <c>Refused</c> falls back — a root takes ONE
    /// path, and the roots of a program are independent.</summary>
    [Test]
    public void ARootNeverTakesBothPaths_AndATwoRootProgramMixesThem()
    {
        var build = CppClosures.Compile(ClosureLoweringContractPrograms.TwoRoots);
        Assert.That(build.Paths.Select(p => (p.Root, p.Path)), Is.EqualTo(new[]
        {
            ("Lowerable", CppClosurePath.Lowered),
            ("Refused", CppClosurePath.ByCopy),
        }), "ClosurePaths lists the roots in function order, each once; Main creates no lambda and is not listed");
        Assert.That(build.AllText.Contains("[=]"), Is.True, "Refused keeps the by-copy lambda");
        Assert.That(build.AllText.Contains("c__Env"), Is.True, "Lowerable got an environment");
    }

    // ---- The test seam itself ---------------------------------------------------------------------------------

    [Test]
    public void ClosurePaths_ListsOnlyRootsThatCreateALambda_AndIsEmptyForAProgramWithNone()
    {
        Assert.That(CppClosures.Compile("Sub Main()\n    Console.WriteLine(1)\nEnd Sub\n").Paths, Is.Empty);

        // a lambda nested in a lambda is ONE root; a lambda-free function is not listed
        var nested = CppClosures.Compile(ClosureLoweringContractPrograms.R21);
        Assert.That(nested.Paths.Select(p => p.Root), Is.EqualTo(new[] { "MakeCounter" }));
    }

    /// <summary>The list belongs to the LAST Generate: a generator reused for a second module reports that
    /// module's roots, not the sum.</summary>
    [Test]
    public void ClosurePaths_IsResetByEachGenerate()
    {
        var gen = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false });
        gen.Generate(CppClosures.Optimized(CppClosurePrograms.M01_L5, CppEntry.Standard));
        Assert.That(gen.ClosurePaths.Select(p => p.Root), Is.EqualTo(new[] { "Main" }));
        gen.Generate(CppClosures.Optimized("Sub Main()\n    Console.WriteLine(1)\nEnd Sub\n", CppEntry.Standard));
        Assert.That(gen.ClosurePaths, Is.Empty, "the second module creates no lambda");
    }

    /// <summary>Names a class member's root the way the C++ backend and the diagnostics spell it:
    /// <c>Class.Member</c>, <c>Class.New</c>, a module procedure by its own name.</summary>
    [Test]
    public void ClosurePaths_NamesRootsClassDotMember()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CppClosures.Compile(BaseConstructorCallLoweringExecutionTests.B1).PathOf("D.New"), Is.EqualTo(CppClosurePath.Lowered));
            Assert.That(CppClosures.Compile(ClosureLoweringContractPrograms.N3).PathOf("D.Run"), Is.EqualTo(CppClosurePath.Lowered));
            Assert.That(CppClosures.Compile(ClosureLoweringContractPrograms.N6).PathOf("D.Twice"), Is.EqualTo(CppClosurePath.Lowered));
            Assert.That(CppClosures.Compile(CppClosurePrograms.MyBaseInLambda).PathOf("Derived.Tag"), Is.EqualTo(CppClosurePath.ByCopy));
            Assert.That(CppClosures.Compile(CppClosurePrograms.EX2).PathOf("Find"), Is.EqualTo(CppClosurePath.Lowered));
        });
    }

    // ---- #226's rule in ComputeInlineRegion: no goto enters a try (ruling D5.2) -----------------------------------------

    private static IEnumerable<TestCaseData> ExitShapes()
    {
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E06).SetName("Exit_E06_exitForOutOfACountedBody");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E07).SetName("Exit_E07_exitForOutOfTheInnerLoop");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E07e).SetName("Exit_E07e");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E07x).SetName("Exit_E07x");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E03).SetName("Exit_E03_userFinally");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E04).SetName("Exit_E04_exitForInAUserTry");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E07f).SetName("Exit_E07f_nestedUserTry");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E13).SetName("Exit_E13");
        yield return new TestCaseData(CppClosurePrograms.EX1).SetName("Exit_EX1_foreachExitInATryFinally");
        yield return new TestCaseData(CppClosurePrograms.EX2).SetName("Exit_EX2_returnFromTheBody");
        yield return new TestCaseData(CppClosurePrograms.EX3).SetName("Exit_EX3_foreachExitThenTheTail");
        yield return new TestCaseData(CppClosurePrograms.CX1).SetName("Exit_CX1_catchVariable");
    }

    /// <summary>
    /// ⭐ The fast killer of "the #226 rule removed from <c>ComputeInlineRegion</c>" (mutant M9). A lowered loop with a
    /// per-iteration environment wraps its body in a try/finally, and an <c>Exit</c> that leaves the region must END it: if
    /// the walk follows the Exit into everything after the loop, the loop's end label lands INSIDE the try and the
    /// emitted <c>goto</c> enters it — which a C++ compiler rejects ("cannot jump from this goto statement to its label")
    /// and nothing but a C++ compiler noticed until this lint read the text. Every program here, in every in-process
    /// mode, jumps only OUT of try blocks.
    /// </summary>
    [TestCaseSource(nameof(ExitShapes))]
    public void NoGoto_EntersATryBlockOrACatchHandler(string source)
    {
        Assert.Multiple(() =>
        {
            foreach (var entry in new[] { CppEntry.Plain, CppEntry.Standard, CppEntry.Aggressive, CppEntry.Project, CppEntry.Split })
                Assert.That(CppGotoLint.IllegalJumps(CppClosures.Compile(source, entry).AllText), Is.Empty, entry.ToString());
        });
    }

    /// <summary>The lint itself: it flags a jump INTO a try block and a jump into a catch handler, and accepts a jump OUT of
    /// one, a jump within one, and the same label name in two functions.</summary>
    [Test]
    public void TheGotoLint_FlagsAJumpIntoATry_AndOnlyThat()
    {
        const string head = "// Forward declarations\n";
        Assert.Multiple(() =>
        {
            Assert.That(CppGotoLint.IllegalJumps(head + "void F() { goto in_try; try { in_try: ; g(); } catch (...) { } }"), Has.Count.EqualTo(1),
                "into a try block");
            Assert.That(CppGotoLint.IllegalJumps(head + "void F() { goto in_catch; try { } catch (const std::exception& e) { in_catch: ; } }"), Has.Count.EqualTo(1),
                "into a catch handler");
            Assert.That(CppGotoLint.IllegalJumps(head + "void F() { try { goto out; } catch (...) { } out: ; }"), Is.Empty, "out of a try block");
            Assert.That(CppGotoLint.IllegalJumps(head + "void F() { try { goto a; a: ; } catch (...) { } }"), Is.Empty, "within a try block");
            Assert.That(CppGotoLint.IllegalJumps(head + "void F() { try { x: ; } catch (...) { } } void G() { goto x; x: ; }"), Is.Empty,
                "the same label name in another function is another label");
            Assert.That(CppGotoLint.IllegalJumps(head + "void F() { auto l = [=]() -> int { goto z; try { } catch (...) { } z: ; return 1; }; }"), Is.Empty,
                "a lambda body is its own scope");
            Assert.That(CppGotoLint.IllegalJumps(head + "void F() { /* try { */ goto k; k: ; const char* s = \"try {\"; }"), Is.Empty,
                "comments and strings are skipped");
        });
    }

    // ---- The verifier on the lowered clone (ruling D5.5) -----------------------------------------------------------

    private const string MakeProgram = """
        Function Make(k As Integer) As Func(Of Integer)
            Return Function() k
        End Function
        Sub Main()
            Console.WriteLine(Make(4)())
        End Sub
        """;

    /// <summary>
    /// The IR verifier runs on ClosureLowering's clone, as it does for MSIL (ruling D5.5). The module handed to
    /// <c>Generate</c> here was never verified — it did not pass through a pipeline after being corrupted — so
    /// the ONLY thing that can report a violation is the check on the clone: Invariant R (the reservation is
    /// complete) fails once <c>Make</c>'s parameter is dropped from its reserved names, and the clone copies that
    /// function's set. A backend that skipped the check would sail through; the uncorrupted module is the control.
    /// </summary>
    [Test]
    [NonParallelizable]
    public void TheVerifier_RunsOnTheLoweredClone()
    {
        var previous = IRVerifier.Mode;
        IRVerifier.Mode = IRVerifierMode.Throw;
        try
        {
            var control = CppClosures.Optimized(MakeProgram, CppEntry.Standard);
            Assert.DoesNotThrow(() => new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(control),
                "control: the same program, uncorrupted, passes the clone's verification");

            var corrupt = CppClosures.Optimized(MakeProgram, CppEntry.Standard);
            var make = IRTempNames.AllFunctions(corrupt).First(f => f.Name == "Make");
            Assert.That(make.ReservedNames.Remove("k"), Is.True, "precondition: 'k' is a reserved parameter name");

            var ex = Assert.Throws<IRVerificationException>(
                () => new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(corrupt));
            Assert.That(ex!.Violations.Select(v => v.Invariant), Does.Contain("R"), ex.Message);
        }
        finally { IRVerifier.Mode = previous; }
    }

    /// <summary>Under the verifier in Throw mode — the test project's default — the lowered clone of every
    /// lowered program raises no violation, through the standard and aggressive pipelines and the project entry
    /// point. (ruling D5.5: the verifier runs on the lowered clone in CI; D09 stays the ONLY pre-existing fire,
    /// see <see cref="TheModuleInitializerLambda_IsTheOnlyPreExistingVerifierFire"/>.)</summary>
    [TestCaseSource(nameof(LoweredRows))]
    [NonParallelizable]
    public void LoweringRaisesNoVerifierViolation_InEveryEntryPoint(string source, string root)
    {
        var previous = IRVerifier.Mode;
        IRVerifier.Mode = IRVerifierMode.Throw;
        try
        {
            Assert.Multiple(() =>
            {
                foreach (var entry in AllEntries)
                    Assert.DoesNotThrow(() => CppClosures.Compile(source, entry), $"{root}, {entry}");
            });
        }
        finally { IRVerifier.Mode = previous; }
    }

    /// <summary>D09: a lambda in a module-level initializer is an orphan no instruction creates — Invariant P(d)
    /// fires on master and on every backend, from the OPTIMIZER's verification (not from this change). Ruling
    /// D5.5: it stays the ONLY pre-existing fire. When it is fixed, this test is deleted and the fallback row
    /// <c>CppClosurePrograms.ModuleInitializer</c> goes with it.</summary>
    [Test]
    [NonParallelizable]
    public void TheModuleInitializerLambda_IsTheOnlyPreExistingVerifierFire()
    {
        var previousMode = IRVerifier.Mode;
        var previousPath = IRVerifier.LogPath;
        var log = Path.Combine(Path.GetTempPath(), "bl-t140-verify-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            IRVerifier.LogPath = log;
            IRVerifier.Mode = IRVerifierMode.Log;
            foreach (var (name, source) in CorpusPrograms())
                CppClosures.TryCompile(source);
            Assert.That(File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>(), Is.Empty,
                "no program of the corpus but D09 may fire the verifier");

            var d09 = CppClosures.Compile(CppClosurePrograms.ModuleInitializer, CppEntry.Standard);
            Assert.That(d09.PathOf("__lambda_0"), Is.EqualTo(CppClosurePath.ByCopy),
                "a lambda outside every function body is its own root, and the lowering never lowers one");
            var fired = File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>();
            Assert.That(fired, Has.Length.EqualTo(1), string.Join("\n", fired));
            Assert.That(fired[0], Does.Contain("Invariant P").And.Contain("__lambda_0").And.Contain("orphan"));
        }
        finally
        {
            IRVerifier.Mode = previousMode;
            IRVerifier.LogPath = previousPath;
            try { File.Delete(log); } catch { /* temp */ }
        }
    }

    // ---- Names (ruling D5.6) ------------------------------------------------------------------------------------

    /// <summary>
    /// NM1: a user class spelled like the lowering's environment (<c>c__Env0</c>), a user class spelled like the
    /// holder (<c>BasicLangClosures</c>), a user function spelled like the thunk's capture (<c>blTarget</c>) and a
    /// user local spelled like an environment field (<c>blArg0</c>) all keep their spelling; the lowering's names
    /// are MINTED around them (<c>c__Env0_1</c>, <c>BasicLangClosures_1</c>, <c>blTarget_1</c>) — ADR-0018's
    /// reserved-name path, never a choice by spelling. (It runs: <c>CppClosureRunTests</c>.)
    /// </summary>
    [Test]
    public void UserNamesLikeTheLoweringsOwn_KeepTheirSpelling_AndTheLoweringsNamesAreMintedAroundThem()
    {
        var text = CppClosures.Compile(CppClosurePrograms.NM1).AllText;
        Assert.Multiple(() =>
        {
            Assert.That(text.Contains("class c__Env0 :"), Is.True, "the user's class keeps its name");
            Assert.That(text.Contains("class BasicLangClosures :"), Is.True, "the user's class keeps its name");
            Assert.That(text.Contains("int32_t blTarget(int32_t x)"), Is.True, "the user's function keeps its name");
            Assert.That(text.Contains("struct BasicLangClosures_1"), Is.True, "the holder is minted around the user's class");
            Assert.That(text.Contains("class c__Env0_1 :"), Is.True, "the environment is minted around the user's class");
            Assert.That(text.Contains("[blTarget_1 = "), Is.True, "the thunk's capture is minted around the user's function");
            Assert.That(System.Text.RegularExpressions.Regex.Matches(text, @"class c__Env0 :").Count, Is.EqualTo(1),
                "exactly one class of that name: no collision");
        });
    }

    // ---- GenerateSplit (the IDE's route) ----------------------------------------------------------------------------

    /// <summary>The IDE's C++ project build calls <c>GenerateSplit</c>, not <c>Generate</c>: it must fill
    /// <c>ClosurePaths</c> and lower exactly as <c>Generate</c> does (the environments nested in their classes,
    /// the holder after every class, in the per-module header).</summary>
    [Test]
    public void GenerateSplit_FillsClosurePaths_AndEmitsTheEnvironments()
    {
        var split = CppClosures.Compile(CppClosurePrograms.M01_L5, CppEntry.Split);
        var combined = CppClosures.Compile(CppClosurePrograms.M01_L5, CppEntry.Project);
        Assert.Multiple(() =>
        {
            Assert.That(split.Paths, Is.EqualTo(combined.Paths));
            Assert.That(split.Paths.Single().Path, Is.EqualTo(CppClosurePath.Lowered));
            Assert.That(split.Files["P.g.h"].Contains("struct BasicLangClosures"), Is.True, "the holder is in the project header");
            Assert.That(split.AllText.Contains("c__Env0"), Is.True);
        });
    }
}
