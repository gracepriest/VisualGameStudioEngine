using System.Collections.Generic;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ADR-0016 D3 (AMENDED, "W2") and what #140 made of it: which lambdas C++ ran by copy, which it refused,
/// and which it now runs lowered.
///
/// <para>C++ used to lower a lambda as <c>[=]</c> — a COPY taken where the lambda is created — so a write to a
/// captured variable that the lambda's copy never sees was a SILENT WRONG ANSWER. #170 refused those
/// programs by name; the rule is ONE rule with two arms, in <c>CppCapabilityChecker.CheckLambdaCaptureWrites</c>
/// over <c>ControlFlowGraph.ExecutionSuccessors</c>: (a) the lambda writes a variable it captures, or (b)
/// the CREATOR writes a captured variable at a point reachable in the creator's CFG from the
/// lambda-creation instruction. #140 (ADR-0019) sends every root through <c>ClosureLowering</c> first, so
/// that rule is now only the soundness proof of a by-copy FALLBACK: it is evaluated for a root the lowering
/// cannot lower, and a root both paths refuse is refused with its text first.</para>
///
/// <para>Groups, in the order the amendment's Falsifier 5 stated them:
/// <list type="bullet">
/// <item><b>5a — the programs W2 refused</b>. Seventeen of the nineteen now RUN, lowered, with VB's own
/// output in all three modes (standard pipeline, aggressive pipeline, <c>CompileProjectFiles</c>); the
/// other two (E09, R12) are refused by BOTH paths and stay in
/// <see cref="BaseConstructorCallCppBothRefusedTests"/>, by name, with the lowering's reason after W2's.</item>
/// <item><b>5b — #140's REGRESSION FENCE</b>: eleven programs that C++ ran before #140 and must keep
/// running with VB's own output. #140 binds the per-iteration instance, not a hoisted local. Each also
/// asserts the PATH its root took, so a path shift is visible even when the output stays right: ten are
/// lowered, E20 (the N9 shape) is the one by-copy fallback.</item>
/// <item><b>5d — the witnesses</b> for the two edge families no other test covers: FE1 (the For Each back
/// edge) and CF1 (Catch → Finally), CR1 (the creation instruction itself) — each now runs.</item>
/// </list></para>
///
/// <para>⚠ Every expectation below is VB's own answer (the other backends' legs of the same tests, and
/// vbc via the t140 oracle) — never what BasicLang happens to print, except the recorded divergences.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class BaseConstructorCallCppRefusalTests
{
    private static string Norm(string s) => FourBackends.Norm(s);

    // ============================================================================================
    // 5a — the programs W2 refused before #140 (17 now run; E09 and R12 are in the both-refused fixture)
    // ============================================================================================

    // t155 P1: a local captured by a Sub lambda, then overwritten by its creator.
    private const string P1 = """
        Delegate Function BinOp(a As Integer, b As Integer) As Integer
        Delegate Sub Notify()
        Sub Main()
            Dim add As BinOp = Function(a As Integer, b As Integer) a + b
            Console.WriteLine(add(2, 3))
            Dim k As Integer = 7
            Dim n As Notify = Sub() Console.WriteLine(k)
            k = 8
            n()
        End Sub
        """;

    // t155 P2: a PARAMETER captured, then overwritten by its own function.
    private const string P2 = """
        Function MakeAdder(k As Integer) As Integer
            Dim f = Function(x As Integer) x + k
            k = k + 1
            Return f(10)
        End Function
        Sub Main()
            Console.WriteLine(MakeAdder(5))
        End Sub
        """;

    // t155 R12_byrefarg: the creator's write is a BYREF ARGUMENT (Bump(n)), not an assignment —
    // the hit vocabulary is "assignment, rename, store, ++/--, ByRef argument".
    internal const string R12_ByRefArg = """
        Sub Bump(ByRef n As Integer)
            n = n + 1
        End Sub
        Sub Main()
            Dim n As Integer = 1
            Dim f = Sub() Console.WriteLine(n)
            Bump(n)
            f()
        End Sub
        """;

    // t185 E3 / E3b (IsIsNotOperatorExecutionTests): the same capture-then-write shape with and
    // without an Is/IsNot operator — the control proves the refusal has nothing to do with Is.
    private const string T185_E3 = """
        Public Class Foo
        End Class

        Sub Main()
            Dim a As Foo = Nothing
            Dim probe As Func(Of Boolean) = Function() a Is Nothing
            Console.WriteLine(probe())
            a = New Foo()
            Console.WriteLine(probe())
            Dim b As Foo = a
            Dim same As Func(Of Foo, Boolean) = Function(y As Foo) y Is b
            Console.WriteLine(same(a))
            Console.WriteLine(same(New Foo()))
        End Sub
        """;

    private const string T185_E3b = """
        Sub Main()
            Dim a As Integer = 0
            Dim probe As Func(Of Boolean) = Function() a = 0
            Console.WriteLine(probe())
            a = 1
            Console.WriteLine(probe())
        End Sub
        """;

    /// <summary>(name, source, VB's output, the root that must take the LOWERED path).</summary>
    private static IEnumerable<TestCaseData> FormerlyRefused()
    {
        // arm (a) — the lambda itself writes what it captures
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.B1, "11 12", "D.New").SetName("5a_arm_a_B1_nowRuns");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.B3, "6\n18", "D.New").SetName("5a_arm_a_B3_nowRuns");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.B4, "6", "D.New").SetName("5a_arm_a_B4_nowRuns");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.C1, "11 12", "D.New").SetName("5a_arm_a_C1_notMyBaseNew_nowRuns");

        // arm (b) — the CREATOR writes it, after the lambda exists
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.B2, "12", "D.New").SetName("5a_arm_b_B2_nowRuns");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.E01_NestedLambda, "21", "D.New").SetName("5a_arm_b_E01_nested_nowRuns");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.E02_TwoParamsOneWritten, "107", "D.New").SetName("5a_arm_b_E02_twoParams_nowRuns");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.E08_ThreeLevels, "211 18", "L1.New").SetName("5a_arm_b_E08_threeLevels_nowRuns");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E03, PerIterationLoopBodyDimProbes.E03Expected, "Main").SetName("5a_arm_b_PerIteration_E03_finally_nowRuns");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E04, PerIterationLoopBodyDimProbes.E04Expected, "Main").SetName("5a_arm_b_PerIteration_E04_exitInTry_nowRuns");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E07f, PerIterationLoopBodyDimProbes.E07fExpected, "Main").SetName("5a_arm_b_PerIteration_E07f_nestedTry_nowRuns");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E12, PerIterationLoopBodyDimProbes.E12Expected, "Main").SetName("5a_arm_b_PerIteration_E12_forControlVariable_nowRuns");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E18, PerIterationLoopBodyDimProbes.E18Expected, "Main").SetName("5a_arm_b_PerIteration_E18_nowRuns");
        yield return new TestCaseData(P1, "5\n8", "Main").SetName("5a_arm_b_t155_P1_nowRuns");
        yield return new TestCaseData(P2, "16", "MakeAdder").SetName("5a_arm_b_t155_P2_parameter_nowRuns");
        yield return new TestCaseData(T185_E3, "True\nFalse\nTrue\nFalse", "Main").SetName("5a_arm_b_t185_E3_nowRuns");
        yield return new TestCaseData(T185_E3b, "True\nFalse", "Main").SetName("5a_arm_b_t185_E3b_noIsControl_nowRuns");
    }

    /// <summary>
    /// Falsifier 5a, moved (#140): every program W2 used to refuse BY NAME — except the two both-refused
    /// ones — now compiles, takes the LOWERED path for the named root, and prints VB's own output in the
    /// standard pipeline, the aggressive pipeline AND the project entry point. They all gave a wrong
    /// answer on C++ before #170, so the ONLY thing that makes "refused" → "runs" safe is that the
    /// output is right; the path assertion keeps a program that happens to run right on the by-copy
    /// fallback from hiding a lowering regression.
    /// </summary>
    [TestCaseSource(nameof(FormerlyRefused))]
    public void FormerlyRefused_NowRunsLoweredWithVbsOutput_InAllThreeModes(string source, string expected, string loweredRoot)
    {
        foreach (var entry in new[] { CppEntry.Standard, CppEntry.Aggressive, CppEntry.Project })
            Assert.That(CppClosures.Compile(source, entry).PathOf(loweredRoot), Is.EqualTo(CppClosurePath.Lowered), $"root '{loweredRoot}', {entry}");
        CppClosures.RunsInAllModes(source, expected);
    }

    // ============================================================================================
    // 5b — #140's REGRESSION FENCE
    // ============================================================================================

    /// <summary>(name, source, VB's expected output, the root, the path it took — measured).</summary>
    private static IEnumerable<TestCaseData> Fence()
    {
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.B5, "7", "D.New", CppClosurePath.Lowered).SetName("Cpp140RegressionFence_B5_readOnlyBaseArgsLambda");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.L2, PerIterationLoopBodyDimProbes.L2Expected, "Main", CppClosurePath.Lowered).SetName("Cpp140RegressionFence_t172_L2");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E05, PerIterationLoopBodyDimProbes.E05Expected, "Main", CppClosurePath.Lowered).SetName("Cpp140RegressionFence_t172_E05");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E06, PerIterationLoopBodyDimProbes.E06Expected, "Main", CppClosurePath.Lowered).SetName("Cpp140RegressionFence_t172_E06");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E07, PerIterationLoopBodyDimProbes.E07Expected, "Main", CppClosurePath.Lowered).SetName("Cpp140RegressionFence_t172_E07");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E07e, PerIterationLoopBodyDimProbes.E07eExpected, "Main", CppClosurePath.Lowered).SetName("Cpp140RegressionFence_t172_E07e");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E07w, PerIterationLoopBodyDimProbes.E07wExpected, "Main", CppClosurePath.Lowered).SetName("Cpp140RegressionFence_t172_E07w");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E07x, PerIterationLoopBodyDimProbes.E07xExpected, "Main", CppClosurePath.Lowered).SetName("Cpp140RegressionFence_t172_E07x");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E10, PerIterationLoopBodyDimProbes.E10Expected, "Main", CppClosurePath.Lowered).SetName("Cpp140RegressionFence_t172_E10");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E13, PerIterationLoopBodyDimProbes.E13Expected, "Main", CppClosurePath.Lowered).SetName("Cpp140RegressionFence_t172_E13");
        // E20: a lambda declares a local spelled like a name its creator also captures (N9). The lowering
        // refuses N9, and W2 admits the root (nothing writes what a lambda captures), so it is the ONE
        // by-copy fallback in the fence — still running with VB's output, as it always did.
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E20, PerIterationLoopBodyDimProbes.E20Expected, "Main", CppClosurePath.ByCopy).SetName("Cpp140RegressionFence_t172_E20");
    }

    /// <summary>
    /// ⭐ #140 REGRESSION FENCE (ADR-0016 D3 amendment, Falsifier 5b and the Obligations; #140 ruling D3). These
    /// programs write a per-iteration <c>Dim</c> after (or around) a lambda that captures it, and
    /// C++'s copy taken at creation EQUALS the per-iteration instance — so they ran with VB's own
    /// output before #140, and W1 (the syntactic "any write" rule) was rejected for refusing nine of
    /// them. #140 replaced <c>[=]</c> with by-reference capture and had to bind the per-iteration
    /// instance, not a hoisted local; they stay green, UNCHANGED, and now ALSO assert the path each root
    /// took (ruling D3: "so a path shift is visible"): ten are lowered, E20 is the by-copy fallback.
    /// Run in the standard pipeline, the aggressive pipeline, and the project entry point.
    /// </summary>
    [TestCaseSource(nameof(Fence))]
    public void Cpp140RegressionFence_RunsWithVbsOutput_InAllThreeModes(string source, string expected, string root, CppClosurePath path)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(source))), Is.EqualTo(expected),
                "C++, standard pipeline");
            Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(source))), Is.EqualTo(expected),
                "C++, aggressive pipeline");
            Assert.That(Norm(BaseConstructorCallLoweringExecutionTests.RunCppViaProjectEntryPoint(source)), Is.EqualTo(expected),
                "C++, project entry point");
            foreach (var entry in new[] { CppEntry.Standard, CppEntry.Aggressive, CppEntry.Project })
                Assert.That(CppClosures.Compile(source, entry).PathOf(root), Is.EqualTo(path), $"the path root '{root}' took, {entry}");
        });
    }

    // ============================================================================================
    // 5d — the witnesses for the two edge families no other test covers
    // ============================================================================================

    private const string FE1_ForEachBackEdge = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            Dim items As New List(Of Integer)()
            items.Add(1)
            items.Add(2)
            items.Add(3)
            Dim x As Integer = 0
            For Each n As Integer In items
                x = x + n
                fs.Add(Function() x)
            Next
            For Each f As Func(Of Integer) In fs
                Console.WriteLine(f())
            Next
        End Sub
        """;

    private const string CR1_SelfReference = """
        Sub Main()
            Dim f As Func(Of Integer, Integer) = Nothing
            f = Function(n As Integer)
                    If n <= 0 Then Return 0
                    Return n + f(n - 1)
                End Function
            Console.WriteLine(f(3))
        End Sub
        """;

    /// <summary>
    /// ⭐ FE1 — the witness for the For Each BACK EDGE. A function-level <c>x</c> written in a For
    /// Each body BEFORE the lambda is created is only reachable from the lambda-creation
    /// instruction through the end-of-body → body-entry edge (the next iteration's
    /// <c>x = x + n</c>). VB prints 6 6 6 (one shared <c>x</c>); the C++ copy would print 1 3 6, which is why
    /// #170 refused it (mutant MF in the shared successor function, killed by this test then). #140 lowers
    /// it — one environment holds the one shared <c>x</c> — so C#, JavaScript and C++ all print 6 6 6.
    /// MSIL did not ASSEMBLE this shape until #225 (<c>unbox.any [mscorlib]System.Func`1&lt;int32&gt;</c>, a
    /// generic token without its <c>class</c>, for the For Each over a <c>List(Of Func(Of Integer))</c>); it
    /// prints 6 6 6 there too now.
    /// </summary>
    [Test]
    public void FE1_ForEachBackEdge_RunsOnCSharpJavaScriptCppAndMsil()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(FE1_ForEachBackEdge)), Is.EqualTo("6\n6\n6"), "C#");
            Assert.That(Norm(VisualGameStudio.Tests.Msil.MsilHarness.RunExpectingSuccess(FE1_ForEachBackEdge)), Is.EqualTo("6\n6\n6"), "MSIL (#225)");
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(FE1_ForEachBackEdge)), Is.EqualTo("6\n6\n6"), "JavaScript");
            Assert.That(CppClosures.Compile(FE1_ForEachBackEdge).PathOf("Main"), Is.EqualTo(CppClosurePath.Lowered), "C++ root 'Main'");
            CppClosures.RunsInAllModes(FE1_ForEachBackEdge, "6\n6\n6");
        });
    }

    private const string CF1_CatchThenFinallyWrite = """
        Sub Main()
            Dim x As Integer = 0
            Dim f As Func(Of Integer) = Nothing
            Try
                Throw New Exception("e")
            Catch ex As Exception
                f = Function() x
            Finally
                x = 5
            End Try
            Console.WriteLine(f())
        End Sub
        """;

    /// <summary>
    /// ⭐ CF1 — the witness for the Catch → Finally EDGE. A lambda created in a CATCH block, whose
    /// captured <c>x</c> is written by the Finally: VB (and JavaScript) print 5 — the lambda sees the
    /// Finally's write — but a C++ copy taken at creation would print 0. The write was reachable from
    /// the creation instruction only through the Catch region's edge to the Finally; #170 refused it by name
    /// (<c>ExecutionSuccessors</c>' <c>Region</c> fix; mutant "Catch → Finally edge dropped", killed by this
    /// test and by <c>ControlFlowGraphExecutionSuccessorsTests.Catch_EveryBlockOfTheCatchRegion_ReachesTheFinally</c>,
    /// which still pin the successor function). #140 lowers it — the environment's <c>x</c> is written by the
    /// Finally and read by the lambda — so C++ prints 5 too, in all three modes.
    /// </summary>
    [Test]
    public void CF1_ALambdaCreatedInACatch_WhoseFinallyWritesIt_RunsOnJavaScriptAndCpp()
    {
        Assert.That(Norm(JavaScriptExecutionTests.RunJs(CF1_CatchThenFinallyWrite)), Is.EqualTo("5"),
            "JavaScript — VB's answer");
        Assert.That(CppClosures.Compile(CF1_CatchThenFinallyWrite).PathOf("Main"), Is.EqualTo(CppClosurePath.Lowered), "C++ root 'Main'");
        CppClosures.RunsInAllModes(CF1_CatchThenFinallyWrite, "5");
    }

    /// <summary>
    /// ⭐ CR1 — the witness for the creation instruction ITSELF. <c>f = Function(n) … f(n - 1)</c>:
    /// the lambda captures <c>f</c> (it calls it as a delegate — an <c>IRCall</c> whose
    /// <c>FunctionName</c> is the variable, not an operand, so <c>LambdaCapturesOf</c> does not
    /// list it) and the assignment that stores the lambda into <c>f</c> IS the creator's write. A copy of
    /// <c>f</c> taken at creation is still empty, so C++ threw <c>bad_function_call</c> at run time and #170
    /// refused it (mutant MC: a search that starts AFTER the creation instruction). Lowered, the lambda reads
    /// <c>f</c> from the environment it shares with its creator, so it recurses: VB prints 6, JavaScript and
    /// C++ both do, and C# does since #136 (it did not compile before: CS1643, the lambda's body lost every block
    /// after its entry block, its return paths with them).
    /// </summary>
    [Test]
    public void CR1_SelfReference_RunsOnCSharpJavaScriptAndCpp()
    {
        // MSIL does not run this shape for an UNRELATED, pre-existing reason — it emits a
        // BadImageFormatException — so it is not asserted.
        Assert.That(Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpForTest(CR1_SelfReference))), Is.EqualTo("6"), "C#");
        Assert.That(Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpAggressiveForTest(CR1_SelfReference))), Is.EqualTo("6"), "C#, aggressive");
        Assert.That(Norm(JavaScriptExecutionTests.RunJs(CR1_SelfReference)), Is.EqualTo("6"), "JavaScript");
        Assert.That(CppClosures.Compile(CR1_SelfReference).PathOf("Main"), Is.EqualTo(CppClosurePath.Lowered), "C++ root 'Main'");
        CppClosures.RunsInAllModes(CR1_SelfReference, "6");
    }
}

/// <summary>
/// The two programs of ADR-0016's 5a list that BOTH C++ closure paths refuse (#140 ruling D1, case 3): the
/// lowering cannot lower them (E09: a lambda in a generic class's constructor — its environment would have to
/// be generic; R12: a captured variable passed ByRef) and the by-copy fallback is unsound for them (the
/// creator writes the capture after the lambda exists). Each is refused with W2's text FIRST — naming the
/// captured variable and its creator, with #140 — then the lowering's reason ("closure lowering cannot lower
/// '&lt;root&gt;' either (#140): C++: …"), in all three modes. Needs no native compiler, so it runs in the fast
/// subset.
/// </summary>
[TestFixture]
public class BaseConstructorCallCppBothRefusedTests
{
    /// <summary>(source, root, the captured variable W2 names, a fragment of the lowering's reason).</summary>
    private static IEnumerable<TestCaseData> BothRefused()
    {
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.E09_GenericDerived, "GBox.New", "p", "generic").SetName("5a_arm_b_E09_generic_bothRefused");
        yield return new TestCaseData(BaseConstructorCallCppRefusalTests.R12_ByRefArg, "Main", "n", "ByRef").SetName("5a_arm_b_t155_R12_byRefArgument_bothRefused");
    }

    [TestCaseSource(nameof(BothRefused))]
    public void BothRefused_W2First_ThenTheLoweringsReason_InAllThreeModes(string source, string root, string variable, string loweringReason)
    {
        Assert.Multiple(() =>
        {
            AssertMode(source, root, variable, loweringReason, "standard", () => BclE2E.CompileToCppOptimized(source));
            AssertMode(source, root, variable, loweringReason, "aggressive", () => BclE2E.CompileToCppAggressive(source));
            AssertMode(source, root, variable, loweringReason, "project", () => BaseConstructorCallLoweringExecutionTests.RunCppViaProjectEntryPoint(source));
        });
    }

    private static void AssertMode(string source, string root, string variable, string reason, string mode, System.Action compile)
    {
        var ex = Assert.Throws<CppCapabilityException>(() => compile());
        CppClosures.AssertBothRefusedMessage(ex!.Message, root, reason, variable, mode);
    }
}
