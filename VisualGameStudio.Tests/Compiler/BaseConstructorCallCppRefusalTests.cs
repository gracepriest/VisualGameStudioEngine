using System.Collections.Generic;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ADR-0016 D3 (AMENDED, "W2"): which lambdas C++ refuses, and which it must keep running.
///
/// <para>C++ lowers a lambda as <c>[=]</c> — a COPY taken where the lambda is created — so a
/// write to a captured variable that the lambda's copy never sees is a SILENT WRONG ANSWER. The
/// rule is ONE rule with two arms, in <c>CppCapabilityChecker.CheckLambdaCaptureWrites</c> over
/// <c>ControlFlowGraph.ExecutionSuccessors</c>: (a) the lambda writes a variable it captures, or
/// (b) the CREATOR writes a captured variable at a point reachable in the creator's CFG from the
/// lambda-creation instruction (that instruction included). It is stated over ANY lambda, never
/// keyed on "inside <c>MyBase.New</c>" (the ADR's Rejected table) — so this file's programs are
/// not all base-constructor shapes.</para>
///
/// <para>Three groups, in the order the amendment's Falsifier 5 states them:
/// <list type="bullet">
/// <item><b>5a — the must-refuse list</b>, refused BY NAME (variable, creator, #140) in ALL THREE
/// modes (standard pipeline, aggressive pipeline, <c>CompileProjectFiles</c>; 5c: "the same set in
/// all three modes"). Neither <c>CppCodeGenerator.Generate</c> nor the project entry point needs a
/// native compiler to REFUSE, so these run everywhere.</item>
/// <item><b>5b — #140's REGRESSION FENCE</b>: eleven programs that C++ runs today and that must keep
/// running with VB's own output after #140 replaces <c>[=]</c> with by-reference capture. #140 must
/// bind the per-iteration instance, not a hoisted local. These are named "#140 regression fence" so
/// the task that lands #140 finds them by search.</item>
/// <item><b>5d — the witnesses</b> for the two edge families no existing test covers: FE1 (the
/// For Each back edge) and CR1 (the creation instruction itself).</item>
/// </list></para>
///
/// <para>⚠ Every message expectation below was measured against a FRESH build of this worktree.
/// Do not trust a scratchpad <c>m-final.txt</c>: several predate the amendment.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class BaseConstructorCallCppRefusalTests
{
    private static string Norm(string s) => FourBackends.Norm(s);

    // ============================================================================================
    // 5a — the must-refuse list
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
    private const string R12_ByRefArg = """
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

    /// <summary>(name, source, captured variable, creator, arm).</summary>
    private static IEnumerable<TestCaseData> MustRefuse()
    {
        // arm (a) — the lambda itself writes what it captures
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.B1, "p", "D.New").SetName("5a_arm_a_B1");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.B3, "p", "D.New").SetName("5a_arm_a_B3");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.B4, "p", "D.New").SetName("5a_arm_a_B4");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.C1, "p", "D.New").SetName("5a_arm_a_C1_notMyBaseNew");

        // arm (b) — the CREATOR writes it, after the lambda exists
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.B2, "p", "D.New").SetName("5a_arm_b_B2");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.E01_NestedLambda, "p", "D.New").SetName("5a_arm_b_E01_nested");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.E02_TwoParamsOneWritten, "b", "D.New").SetName("5a_arm_b_E02_twoParams");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.E08_ThreeLevels, "a", "L1.New").SetName("5a_arm_b_E08_threeLevels");
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.E09_GenericDerived, "p", "GBox.New").SetName("5a_arm_b_E09_generic");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E03, "x", "Main").SetName("5a_arm_b_PerIteration_E03_finally");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E04, "x", "Main").SetName("5a_arm_b_PerIteration_E04_exitInTry");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E07f, "x", "Main").SetName("5a_arm_b_PerIteration_E07f_nestedTry");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E12, "i", "Main").SetName("5a_arm_b_PerIteration_E12_forControlVariable");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E18, "i", "Main").SetName("5a_arm_b_PerIteration_E18");
        yield return new TestCaseData(P1, "k", "Main").SetName("5a_arm_b_t155_P1");
        yield return new TestCaseData(P2, "k", "MakeAdder").SetName("5a_arm_b_t155_P2_parameter");
        yield return new TestCaseData(R12_ByRefArg, "n", "Main").SetName("5a_arm_b_t155_R12_byRefArgument");
        yield return new TestCaseData(T185_E3, "a", "Main").SetName("5a_arm_b_t185_E3");
        yield return new TestCaseData(T185_E3b, "a", "Main").SetName("5a_arm_b_t185_E3b_noIsControl");
    }

    /// <summary>
    /// Falsifier 5a: every program on the must-refuse list is refused BY NAME — the captured
    /// variable, its creator, and #140 — in the standard pipeline, the aggressive pipeline AND the
    /// project entry point. A refusal that named the wrong variable would send the user to fix the
    /// wrong line, so the variable and creator are asserted, not just the code.
    /// </summary>
    [TestCaseSource(nameof(MustRefuse))]
    public void MustRefuse_ByName_InAllThreeModes(string source, string variable, string creator)
        => BaseConstructorCallLoweringExecutionTests.AssertCppRefusedByNameInAllThreeModes(source, variable, creator);

    // ============================================================================================
    // 5b — #140's REGRESSION FENCE
    // ============================================================================================

    /// <summary>(name, source, VB's expected output).</summary>
    private static IEnumerable<TestCaseData> Fence()
    {
        yield return new TestCaseData(BaseConstructorCallLoweringExecutionTests.B5, "7").SetName("Cpp140RegressionFence_B5_readOnlyBaseArgsLambda");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.L2, PerIterationLoopBodyDimProbes.L2Expected).SetName("Cpp140RegressionFence_t172_L2");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E05, PerIterationLoopBodyDimProbes.E05Expected).SetName("Cpp140RegressionFence_t172_E05");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E06, PerIterationLoopBodyDimProbes.E06Expected).SetName("Cpp140RegressionFence_t172_E06");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E07, PerIterationLoopBodyDimProbes.E07Expected).SetName("Cpp140RegressionFence_t172_E07");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E07e, PerIterationLoopBodyDimProbes.E07eExpected).SetName("Cpp140RegressionFence_t172_E07e");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E07w, PerIterationLoopBodyDimProbes.E07wExpected).SetName("Cpp140RegressionFence_t172_E07w");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E07x, PerIterationLoopBodyDimProbes.E07xExpected).SetName("Cpp140RegressionFence_t172_E07x");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E10, PerIterationLoopBodyDimProbes.E10Expected).SetName("Cpp140RegressionFence_t172_E10");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E13, PerIterationLoopBodyDimProbes.E13Expected).SetName("Cpp140RegressionFence_t172_E13");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E20, PerIterationLoopBodyDimProbes.E20Expected).SetName("Cpp140RegressionFence_t172_E20");
    }

    /// <summary>
    /// ⭐ #140 REGRESSION FENCE (ADR-0016 D3 amendment, Falsifier 5b and the Obligations). These
    /// programs write a per-iteration <c>Dim</c> after (or around) a lambda that captures it, and
    /// C++'s copy taken at creation EQUALS the per-iteration instance — so they run with VB's own
    /// output today, and W1 (the syntactic "any write" rule) was rejected for refusing nine of
    /// them. The per-iteration cut and the Dim-initializer rule exist to keep them running.
    /// When #140 replaces <c>[=]</c> with by-reference capture it must bind the per-iteration
    /// instance, not a hoisted local, or these go wrong; when it deletes W2 they must stay green.
    /// Run in the standard pipeline, the aggressive pipeline, and the project entry point.
    /// </summary>
    [TestCaseSource(nameof(Fence))]
    public void Cpp140RegressionFence_RunsWithVbsOutput_InAllThreeModes(string source, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(source))), Is.EqualTo(expected),
                "C++, standard pipeline");
            Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(source))), Is.EqualTo(expected),
                "C++, aggressive pipeline");
            Assert.That(Norm(BaseConstructorCallLoweringExecutionTests.RunCppViaProjectEntryPoint(source)), Is.EqualTo(expected),
                "C++, project entry point");
        });
    }

    /// <summary>
    /// E16 (two sibling loops each declaring <c>Dim x</c>) is neither refused nor run: W2 lets it
    /// through — it is not a #170 finding — and the C++ compiler rejects the redefinition. It STAYS
    /// a named clang failure (task #229). If W2 ever REFUSED it, the "W2 on BodyLocals alone"
    /// rejected alternative would have crept back in (the amendment measured it: E16 was one of
    /// that rule's collateral cases).
    /// </summary>
    [Test]
    public void E16_StaysANamedClangFailure_NeitherRefusedNorRun()
    {
        string cpp = null;
        Assert.That(() => cpp = BclE2E.CompileToCppOptimized(PerIterationLoopBodyDimProbes.E16), Throws.Nothing,
            "W2 must NOT refuse E16 — it is task #229's clang redefinition, not a stale capture");
        Assert.That(() => BclE2E.CompileRun(cpp), Throws.Exception,
            "task #229 — two sibling `Dim x` loops are a C++ redefinition");
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
    /// <c>x = x + n</c>). VB prints 6 6 6 (one shared <c>x</c>); the C++ copy would print 1 3 6.
    /// Without that edge in the shared successor function the search finds nothing after the
    /// creation and the wrong answer escapes (mutant MF, killed by this test). C# and JavaScript
    /// print VB's answer; MSIL does not ASSEMBLE this shape (<c>unbox.any Func`1&lt;int32&gt;</c> — a
    /// For Each over a <c>List(Of Func(Of Integer))</c>, a pre-existing generic-delegate gap
    /// unrelated to #170), which is why MSIL is not asserted here.
    /// </summary>
    [Test]
    public void FE1_ForEachBackEdge_IsRefused_AndTheOtherBackendsPrintVbsAnswer()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(FE1_ForEachBackEdge)), Is.EqualTo("6\n6\n6"), "C#");
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(FE1_ForEachBackEdge)), Is.EqualTo("6\n6\n6"), "JavaScript");
        });
        BaseConstructorCallLoweringExecutionTests.AssertCppRefusedByNameInAllThreeModes(FE1_ForEachBackEdge, "x", "Main");
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
    /// Finally's write — but a C++ copy taken at creation would print 0. The write is reachable from
    /// the creation instruction only through the Catch region's edge to the Finally, so it is refused
    /// BY NAME (variable <c>x</c>, creator <c>Main</c>, arm (b): "'Main' writes it", #140) in all three
    /// modes. This was a wrong answer that ESCAPED W2 until <c>ExecutionSuccessors</c>' <c>Region</c>
    /// was fixed to always contain its own entry block — a Catch is a stop block for its own region, so
    /// the region used to be empty and no Catch → Finally edge was ever added (mutant "Catch → Finally
    /// edge dropped" restores that, and is killed by this test and by
    /// <c>ControlFlowGraphExecutionSuccessorsTests.Catch_EveryBlockOfTheCatchRegion_ReachesTheFinally</c>).
    /// </summary>
    [Test]
    public void CF1_ALambdaCreatedInACatch_WhoseFinallyWritesIt_IsRefusedByName()
    {
        Assert.That(Norm(JavaScriptExecutionTests.RunJs(CF1_CatchThenFinallyWrite)), Is.EqualTo("5"),
            "JavaScript — VB's answer");

        var ex = Assert.Throws<CppCapabilityException>(() => BclE2E.CompileToCppOptimized(CF1_CatchThenFinallyWrite));
        Assert.That(ex!.Message, Does.Contain("captures 'x' of 'Main'").And.Contain("'Main' writes it").And.Contain("#140"),
            "arm (b): the creator's Finally writes the captured x.\n" + ex.Message);

        BaseConstructorCallLoweringExecutionTests.AssertCppRefusedByNameInAllThreeModes(CF1_CatchThenFinallyWrite, "x", "Main");
    }

    /// <summary>
    /// ⭐ CR1 — the witness for the creation instruction ITSELF. <c>f = Function(n) … f(n - 1)</c>:
    /// the lambda captures <c>f</c> (it calls it as a delegate — an <c>IRCall</c> whose
    /// <c>FunctionName</c> is the variable, not an operand, so <c>LambdaCapturesOf</c> does not
    /// list it) and the assignment that stores the lambda into <c>f</c> IS the creator's write. The
    /// write sits on the creation instruction itself, so a search that starts AFTER it (mutant MC)
    /// misses it and C++ throws <c>bad_function_call</c> at run time. VB prints 6.
    /// </summary>
    [Test]
    public void CR1_SelfReference_IsRefused_AndJavaScriptPrintsVbsAnswer()
    {
        // C# and MSIL do not run this shape for UNRELATED, pre-existing reasons — C# empties a
        // multi-statement lambda body (`() => { ; }`, CS1643: #136) and MSIL emits a
        // BadImageFormatException — so only JavaScript is asserted as the oracle leg.
        Assert.That(Norm(JavaScriptExecutionTests.RunJs(CR1_SelfReference)), Is.EqualTo("6"), "JavaScript");
        BaseConstructorCallLoweringExecutionTests.AssertCppRefusedByNameInAllThreeModes(CR1_SelfReference, "f", "Main");
    }
}
