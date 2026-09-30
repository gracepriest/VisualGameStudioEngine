using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.CodeGen.MSIL;
using VisualGameStudio.Tests.Msil;
using VisualGameStudio.Tests.Native;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ADR-0016 (#170): <c>MyBase.New(...)</c> becomes <c>IRBaseConstructorCall</c>, an instruction in
/// the constructor's entry block rather than an off-stream list
/// (<c>IRConstructor.BaseConstructorArgs</c>) no block held. Execution coverage for the ruling's
/// own probe corpus — B1-B5 (lambda arguments), W1/W2 (computed pure arguments, absorbing #240),
/// and C1 (the non-<c>MyBase.New</c> control: the SAME lambda-writes-a-captured-parameter shape
/// through an ordinary <c>Sub</c> call, proving the C++ fault the ADR names is IR-level, not
/// <c>MyBase.New</c>-specific) — each run through the STANDARD optimizer pipeline, the AGGRESSIVE
/// one, and <c>BasicCompiler.CompileProjectFiles</c> (the project entry point a Release
/// <c>.blproj</c> build and the IDE's build service both use), per CLAUDE.md's "test both entry
/// points" / "validate codegen through the optimizer" rules. Expected values are verbatim from
/// the implementer's <c>S/t170/probes/*.exp</c> and <c>S/t170/ctrl*/*.exp</c> (VB's own answer),
/// cross-checked against this worktree's own freshly-built CLI before being pinned — several of
/// the scratchpad's own <c>m-final.txt</c> measurement files predate the D3 amendment and read
/// stale for any arm-(b) shape; never trust one without a fresh compile.
///
/// <para>C++ per the amended D3 (W2, this ADR's own amendment): B1, B3, B4 and C1 are refused by
/// name (arm (a) — the lambda writes what it captures, directly); B2 is refused by name (arm (b)
/// — the CREATOR writes the captured variable after the lambda is created); B5, W1 and W2 run,
/// matching VB. See <see cref="BaseConstructorCallCppRefusalTests"/> for the amendment's must-
/// refuse list (5a) and #140's regression fence (5b).</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class BaseConstructorCallLoweringExecutionTests
{
    private static string Norm(string s) => FourBackends.Norm(s);

    // ============================================================================================
    // Shared plumbing
    // ============================================================================================

    /// <summary>
    /// The project-build entry point (<c>BasicCompiler.CompileProjectFiles</c>, aggressive
    /// options — what the CLI's own <c>-c Release</c> and the IDE's build service both use), as
    /// opposed to the single-file path every other helper here goes through. Mirrors
    /// <c>NameBindingExecutionTests.RunViaProjectEntryPoint</c> / <c>PerIterationLoopBodyDimTests</c>'
    /// own copy (task #169's convention) — kept as its own private copy per CLAUDE.md's "change
    /// shared source once" rule not applying here: this is TEST plumbing, not production source
    /// shared across consumers.
    ///
    /// <para>The "cpp" case needs no native compiler to detect a REFUSAL: <c>CppCodeGenerator.Generate</c>
    /// runs <c>CppCapabilityChecker</c> itself, before any C++ text is written, so calling this
    /// with <c>backend: "cpp"</c> on a refused program throws <see cref="CppCapabilityException"/>
    /// straight out of this method — a pure in-process assertion, no MSVC/clang needed. Only a
    /// program that is NOT refused reaches <see cref="BclE2E.CompileRun"/>, which DOES need one
    /// (and <c>Assert.Ignore</c>s without it).</para>
    /// </summary>
    private static string RunViaProjectEntryPoint(string backend, string source)
    {
        var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true });
        var dir = Path.Combine(Path.GetTempPath(), "bl-t170-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Main.bas");
            File.WriteAllText(path, source);

            var result = compiler.CompileProjectFiles(new List<string> { path });
            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the project entry point produced no combined IR");
            var ir = result.CombinedIR;

            return backend switch
            {
                "csharp" => FourBackends.RunEmittedCSharpText(new ImprovedCSharpCodeGenerator().Generate(ir)),
                "cpp" => BclE2E.CompileRun(
                    new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(ir)),
                "javascript" => JavaScriptExecutionTests.RunNodeScript(
                    new JavaScriptCodeGenerator().Generate(ir)),
                "msil" => MsilHarness.RunIlExpectingSuccess(new MSILCodeGenerator().Generate(ir), "T"),
                _ => throw new ArgumentException("unknown backend " + backend),
            };
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    /// <summary>The C++ leg of <see cref="RunViaProjectEntryPoint"/>, for the sibling refusal fixture.</summary>
    internal static string RunCppViaProjectEntryPoint(string source) => RunViaProjectEntryPoint("cpp", source);

    /// <summary>C#, JavaScript and MSIL agree with VB in ALL THREE modes; C++ REFUSES BY NAME in
    /// all three too (arm (a) or (b) — the caller states which by the message it expects).</summary>
    private static void AssertRunsEverywhereExceptCpp(string source, string expected, string cppVariable, string cppCreator)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(source)), Is.EqualTo(expected), "C#, standard");
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(source)), Is.EqualTo(expected), "JavaScript");
            Assert.That(Norm(MsilHarness.RunExpectingSuccess(source)), Is.EqualTo(expected), "MSIL, standard");
            Assert.That(Norm(FourBackends.RunEmittedCSharpAggressive(source)), Is.EqualTo(expected), "C#, aggressive");
            Assert.That(Norm(FourBackends.RunAggressiveJs(source)), Is.EqualTo(expected), "JavaScript, aggressive");
            Assert.That(Norm(MsilHarness.RunAggressiveExpectingSuccess(source)), Is.EqualTo(expected), "MSIL, aggressive");
            Assert.That(Norm(RunViaProjectEntryPoint("csharp", source)), Is.EqualTo(expected), "C#, project entry point");
            Assert.That(Norm(RunViaProjectEntryPoint("javascript", source)), Is.EqualTo(expected), "JavaScript, project entry point");
            Assert.That(Norm(RunViaProjectEntryPoint("msil", source)), Is.EqualTo(expected), "MSIL, project entry point");

            AssertCppRefusedByNameInAllThreeModes(source, cppVariable, cppCreator);
        });
    }

    /// <summary>The C++ leg alone, refused by name, checked in the standard pipeline, the
    /// aggressive pipeline, and the project entry point (5a/5c's "same set in all three
    /// modes").</summary>
    internal static void AssertCppRefusedByNameInAllThreeModes(string source, string variable, string creator)
    {
        var standard = Assert.Throws<CppCapabilityException>(() => BclE2E.CompileToCppOptimized(source));
        Assert.That(standard!.Message, Does.Contain($"captures '{variable}' of '{creator}'").And.Contain("#140"),
            "C++, standard pipeline.\n" + standard.Message);

        var aggressive = Assert.Throws<CppCapabilityException>(() => BclE2E.CompileToCppAggressive(source));
        Assert.That(aggressive!.Message, Does.Contain($"captures '{variable}' of '{creator}'").And.Contain("#140"),
            "C++, aggressive pipeline.\n" + aggressive.Message);

        var proj = Assert.Throws<CppCapabilityException>(() => RunViaProjectEntryPoint("cpp", source));
        Assert.That(proj!.Message, Does.Contain($"captures '{variable}' of '{creator}'").And.Contain("#140"),
            "C++, project entry point.\n" + proj.Message);
    }

    /// <summary>All four backends agree with VB in all three modes — the shape RUNS everywhere. The
    /// standard and aggressive legs are the suite's own shared four-backend runners
    /// (<see cref="FourBackends.RunsOnEveryBackend"/> / <see cref="FourBackends.RunsOnEveryBackendAggressive"/>,
    /// which keep the MSIL/C++/JS skip gates); the project entry point is this fixture's own.</summary>
    private static void AssertRunsEverywhereIncludingCpp(string source, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(RunViaProjectEntryPoint("csharp", source)), Is.EqualTo(expected), "C#, project entry point");
            Assert.That(Norm(RunViaProjectEntryPoint("javascript", source)), Is.EqualTo(expected), "JavaScript, project entry point");
            Assert.That(Norm(RunViaProjectEntryPoint("cpp", source)), Is.EqualTo(expected), "C++, project entry point");
            Assert.That(Norm(RunViaProjectEntryPoint("msil", source)), Is.EqualTo(expected), "MSIL, project entry point");
        });
        FourBackends.RunsOnEveryBackend(source, expected);
        FourBackends.RunsOnEveryBackendAggressive(source, expected);
    }

    // ============================================================================================
    // B1-B5: a lambda passed as a MyBase.New argument (ADR-0016's own flagship shapes)
    // ============================================================================================

    internal const string B1 = """
        Class Base
            Public F As Action
            Public Sub New(f As Action)
                Me.F = f
            End Sub
            Public Sub Fire()
                F()
            End Sub
        End Class

        Class D
            Inherits Base
            Public A As Integer
            Public B As Integer
            Public Sub New(p As Integer)
                MyBase.New(Sub() p = p + 1)
                Dim q As Integer = 10
                A = p + q
                Fire()
                B = p + q
            End Sub
        End Class

        Sub Main()
            Dim d As New D(1)
            Console.WriteLine(d.A & " " & d.B)
        End Sub
        """;

    /// <summary>B1: the base-args lambda shares ITS captured parameter with the constructor
    /// BODY — VB's own semantics, and the fault ADR-0016 D1 fixes (the lambda creation used to be
    /// off-stream, so the capture scan never saw it: CS0103 `__lambda_0` on C#, a silent wrong
    /// answer on C++, a JS TDZ ReferenceError, a named MSIL refusal). Arm (a) on C++: the lambda
    /// writes `p` directly.</summary>
    [Test]
    public void B1_LambdaWritesTheCapturedConstructorParameter()
        => AssertRunsEverywhereExceptCpp(B1, "11 12", "p", "D.New");

    internal const string B2 = """
        Class Base
            Public G As Func(Of Integer)
            Public Sub New(g As Func(Of Integer))
                Me.G = g
            End Sub
            Public Function Get2() As Integer
                Return G()
            End Function
        End Class

        Class D
            Inherits Base
            Public Sub New(p As Integer)
                MyBase.New(Function() p * 2)
                p = p + 5
            End Sub
        End Class

        Sub Main()
            Dim d As New D(1)
            Console.WriteLine(d.Get2())
        End Sub
        """;

    /// <summary>B2: the lambda itself only READS `p` — the write (`p = p + 5`) is the CREATOR's
    /// own, after the lambda is created. Arm (b) on C++, not arm (a): the amendment's whole
    /// reason to exist (D3 as first ruled left this one a silent wrong answer, 2 instead of 12 —
    /// see the amendment's own falsifier 5, "0 regressions / all 15 wrong answers caught").</summary>
    [Test]
    public void B2_TheCreatorWritesWhatTheLambdaOnlyReads()
        => AssertRunsEverywhereExceptCpp(B2, "12", "p", "D.New");

    internal const string B3 = """
        Class Base
            Public F As Action
            Public Sub New(f As Action)
                Me.F = f
            End Sub
            Public Sub Fire()
                F()
            End Sub
        End Class

        Class D
            Inherits Base
            Public Sub New(p As Integer)
                MyBase.New(Sub() p = p * 3)
                Fire()
                Console.WriteLine(p)
                Fire()
                Console.WriteLine(p)
            End Sub
        End Class

        Sub Main()
            Dim d As New D(2)
            d.Fire()
        End Sub
        """;

    /// <summary>B3: the SAME lambda invoked three times (twice from the constructor body, once
    /// from <c>Main</c> after construction) — proves the shared closure is the SAME storage on
    /// every call, not a fresh copy per invocation. Arm (a).</summary>
    [Test]
    public void B3_TheSameLambdaInvokedRepeatedlyMutatesOneSharedClosure()
        => AssertRunsEverywhereExceptCpp(B3, "6\n18", "p", "D.New");

    internal const string B4 = """
        Class Base
            Public F As Action
            Public Sub New(f As Action)
                Me.F = f
            End Sub
            Public Sub Fire()
                F()
            End Sub
        End Class

        Class D
            Inherits Base
            Public Total As Integer
            Public Sub New(p As Integer)
                MyBase.New(Sub() p = p + 1)
                Dim i As Integer
                For i = 1 To 3
                    Fire()
                    Total = Total + p
                Next
            End Sub
        End Class

        Sub Main()
            Dim d As New D(0)
            Console.WriteLine(d.Total)
        End Sub
        """;

    /// <summary>B4: the base-args lambda invoked from INSIDE a loop in the constructor body.
    /// Arm (a).</summary>
    [Test]
    public void B4_TheBaseArgsLambdaIsInvokedFromALoopInTheConstructorBody()
        => AssertRunsEverywhereExceptCpp(B4, "6", "p", "D.New");

    internal const string B5 = """
        Class Base
            Public G As Func(Of Integer)
            Public Sub New(g As Func(Of Integer))
                Me.G = g
            End Sub
        End Class

        Class D
            Inherits Base
            Public Sub New(p As Integer, s As String)
                MyBase.New(Function() p + s.Length)
            End Sub
        End Class

        Sub Main()
            Dim d As New D(4, "abc")
            Console.WriteLine(d.G())
        End Sub
        """;

    /// <summary>B5: READ-ONLY — the lambda captures TWO parameters and writes neither. Runs on
    /// EVERY backend including C++: a copy-captured, never-written closure is exactly what
    /// <c>[=]</c> gets right. The amendment's own contrast case against B1-B4.</summary>
    [Test]
    public void B5_AReadOnlyBaseArgsLambda_RunsOnEveryBackendIncludingCpp()
        => AssertRunsEverywhereIncludingCpp(B5, "7");

    // ============================================================================================
    // W1/W2 — a COMPUTED (non-lambda) MyBase.New argument, absorbing #240
    // ============================================================================================

    internal const string W1 = """
        Class Base
            Public N As Integer
            Public Sub New(n As Integer)
                Me.N = n
            End Sub
        End Class

        Class D
            Inherits Base
            Public A As Integer
            Public Sub New(p As Integer)
                MyBase.New(p + 1)
            End Sub
        End Class

        Sub Main()
            Dim d As New D(1)
            Console.WriteLine(d.N)
        End Sub
        """;

    /// <summary>W1 (#240's own flagship): a PURE ARITHMETIC computed argument — no lambda at all.
    /// Before #170 this was CS0103 `t0` on C#, a JS TDZ error, a named MSIL refusal; C++ alone ran
    /// it (ADR-0015 E11). Runs on all four backends now.</summary>
    [Test]
    public void W1_APureArithmeticComputedArgument_RunsOnEveryBackend()
        => AssertRunsEverywhereIncludingCpp(W1, "2");

    internal const string W2 = """
        Class Base
            Public N As Integer
            Public Sub New(n As Integer)
                Me.N = n
            End Sub
        End Class

        Class D
            Inherits Base
            Public A As Integer
            Public Sub New(p As Integer)
                MyBase.New(Twice(p))
            End Sub
        End Class

        Function Twice(x As Integer) As Integer
            Return x * 2
        End Function

        Sub Main()
            Dim d As New D(1)
            Console.WriteLine(d.N)
        End Sub
        """;

    /// <summary>W2: a computed argument that is a FUNCTION CALL rather than an operator
    /// expression — the same #240 fault, the same fix (D2: "a lambda IS a computed argument").
    /// Runs on all four backends.</summary>
    [Test]
    public void W2_AComputedCallArgument_RunsOnEveryBackend()
        => AssertRunsEverywhereIncludingCpp(W2, "2");

    // ============================================================================================
    // C1 — the non-MyBase.New control: the SAME lambda-writes-a-captured-parameter shape through
    // an ORDINARY Sub call. Proves the fault (and the fix) is a general IR-level one — nothing
    // about it is specific to MyBase.New — and that ADR-0016 D3's C++ rule is stated generally
    // ("any lambda that writes a captured variable"), never scoped to base-constructor position.
    // ============================================================================================

    internal const string C1 = """
        Class Base
            Public F As Action
            Public Sub SetIt(f As Action)
                Me.F = f
            End Sub
            Public Sub Fire()
                F()
            End Sub
        End Class

        Class D
            Inherits Base
            Public A As Integer
            Public B As Integer
            Public Sub New(p As Integer)
                SetIt(Sub() p = p + 1)
                Dim q As Integer = 10
                A = p + q
                Fire()
                B = p + q
            End Sub
        End Class

        Sub Main()
            Dim d As New D(1)
            Console.WriteLine(d.A & " " & d.B)
        End Sub
        """;

    /// <summary>
    /// C1: byte-identical to B1 in shape and answer, but the lambda is an argument to an ORDINARY
    /// <c>Sub</c> call (<c>SetIt</c>), not <c>MyBase.New</c> — this was never broken on C#,
    /// JavaScript or MSIL (the argument was always in the instruction stream; only the base-call
    /// SITE was off-stream). Its ONLY divergence from B1 is the C++ refusal, which the ADR states
    /// as a GENERAL rule over "any lambda that writes a captured variable" — never one keyed on
    /// "inside MyBase.New's argument list" (the ADR's own Rejected table: "a second list #140 must
    /// remember to delete"). C1 is the proof that rule is general, not position-scoped.
    /// </summary>
    [Test]
    public void C1_TheSameShapeThroughAnOrdinarySubCall_IsNeverBrokenExceptOnCpp()
        => AssertRunsEverywhereExceptCpp(C1, "11 12", "p", "D.New");

    // ============================================================================================
    // Edge probes (S/t170/edge), each pinned to its measured answer. Re-measured against a FRESH
    // build of this worktree before being pinned here — several of the scratchpad's own
    // measurement files (edge/m-final.txt included) predate the D3 amendment and read arm-(b)
    // shapes as "RAN WRONG" rather than refused; never trust one without a fresh compile.
    // ============================================================================================

    internal const string E01_NestedLambda = """
        Class Base
            Public F As Func(Of Integer)
            Public Sub New(f As Func(Of Integer))
                Me.F = f
            End Sub
        End Class

        Function Apply(g As Func(Of Integer, Integer), v As Integer) As Integer
            Return g(v)
        End Function

        Class D
            Inherits Base
            Public Sub New(p As Integer)
                MyBase.New(Function() Apply(Function(x As Integer) x + p, 1))
                p = p * 10
            End Sub
        End Class

        Sub Main()
            Dim d As New D(2)
            Console.WriteLine(d.F())
        End Sub
        """;

    /// <summary>
    /// E01: a lambda nested INSIDE the base-args lambda — the outer lambda's own body calls
    /// <c>Apply</c>, which invokes an INNER lambda that reads (not writes) `p`. Neither lambda
    /// writes `p` itself; the CREATOR does, in the very next statement (<c>p = p * 10</c>), after
    /// the outer lambda is created — arm (b). C#/JavaScript run 21. MSIL hits a PRE-EXISTING,
    /// unrelated gap (#241): a lambda nested in a lambda inside a class member fails <c>ilasm</c>
    /// ("undefined class ...&lt;&gt;c__Env0...&lt;&gt;c__Env2") — measured identically on a
    /// constructor-BODY shape before #170; base-args nesting only makes it REACHABLE (it used to
    /// stop at the old, position-independent MSIL refusal first). Filed, not fixed here.
    /// </summary>
    [Test]
    public void E01_NestedLambda_CSharpAndJavaScriptRun_CppRefusedByName_MsilPinnedForTask241()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(E01_NestedLambda)), Is.EqualTo("21"), "C#");
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(E01_NestedLambda)), Is.EqualTo("21"), "JavaScript");

            var cpp = Assert.Throws<CppCapabilityException>(() => BclE2E.CompileToCppOptimized(E01_NestedLambda));
            Assert.That(cpp!.Message, Does.Contain("captures 'p' of 'D.New'").And.Contain("#140"),
                "C++.\n" + cpp.Message);

            var msil = Msil.MsilHarness.Run(E01_NestedLambda);
            Assert.That(msil.Outcome, Is.EqualTo(Msil.MsilHarness.MsilOutcome.AssembleFailed),
                "MSIL — pre-existing #241, unaffected by #170. On a machine with no ilasm this " +
                "assertion is unreachable (Run skips first); written correctly for a Windows run.");
            Assert.That(msil.Detail, Does.Contain("undefined class").And.Contain("<>c__Env"),
                "MSIL.\n" + msil.Detail);
        });
    }

    internal const string E02_TwoParamsOneWritten = """
        Class Base
            Public F As Func(Of Integer)
            Public Sub New(f As Func(Of Integer))
                Me.F = f
            End Sub
        End Class

        Class D
            Inherits Base
            Public Sub New(a As Integer, b As Integer)
                MyBase.New(Function() a * 100 + b)
                b = b + 5
            End Sub
        End Class

        Sub Main()
            Dim d As New D(1, 2)
            Console.WriteLine(d.F())
        End Sub
        """;

    /// <summary>E02: TWO parameters captured, only ONE written — and only after the lambda's
    /// creation. The captured-but-untouched parameter (`a`) must not spuriously trip the refusal:
    /// the message names `b`, never `a`. Arm (b).</summary>
    [Test]
    public void E02_TwoParametersCapturedOnlyOneWritten_RefusesNamingOnlyTheWrittenOne()
        => AssertRunsEverywhereExceptCpp(E02_TwoParamsOneWritten, "107", "b", "D.New");

    internal const string E06_Barrier = """
        Class Base
            Public N As Integer
            Public Sub New(n As Integer, f As Action)
                Me.N = n
                f()
            End Sub
        End Class

        Class D
            Inherits Base
            Public A As Integer
            Public B As Integer
            Public Sub New(p As Integer, q As Integer)
                MyBase.New(p * q + 1, Sub() p = p + 10)
                A = p * q + 1
                B = q * 2 + 1
            End Sub
        End Class

        Sub Main()
            Dim d As New D(1, 3)
            Console.WriteLine(d.N & " " & d.A & " " & d.B)
        End Sub
        """;

    /// <summary>
    /// ⭐ C2's own executable claim: <c>IRBaseConstructorCall</c> is a FULL BARRIER. `p * q + 1`
    /// is computed on BOTH sides of the base call — once as the first argument (before the base
    /// constructor invokes the SECOND argument, a lambda that writes `p`), once again in the
    /// constructor body — and a CSE/CopyProp merge across the barrier would fold both to the SAME
    /// (stale) value. VB's answer keeps them apart: <c>4 34 7</c> (N=1*3+1=4 before the write;
    /// A=(1+10)*3+1=34 after it; B=3*2+1=7, untouched by `p`). Arm (a) on C++: the lambda writes
    /// `p` directly.
    /// </summary>
    [Test]
    public void E06_TheBaseCallIsAFullBarrierAcrossCSEAndCopyProp()
        => AssertRunsEverywhereExceptCpp(E06_Barrier, "4 34 7", "p", "D.New");

    internal const string E08_ThreeLevels = """
        Class L0
            Public F0 As Func(Of Integer)
            Public Sub New(f As Func(Of Integer))
                F0 = f
            End Sub
        End Class

        Class L1
            Inherits L0
            Public F1 As Func(Of Integer)
            Public Sub New(a As Integer, g As Func(Of Integer))
                MyBase.New(Function() a + 1)
                F1 = g
                a = a * 2
            End Sub
        End Class

        Class L2
            Inherits L1
            Public Sub New(b As Integer)
                MyBase.New(b + 100, Function() b * 3)
                b = b + 1
            End Sub
        End Class

        Sub Main()
            Dim x As New L2(5)
            Console.WriteLine(x.F0() & " " & x.F1())
        End Sub
        """;

    /// <summary>
    /// E08: THREE levels of inheritance, each with its OWN base-args lambda and its OWN
    /// post-call write to the SAME name the lambda captured (<c>L1.New</c>'s `a`, <c>L2.New</c>'s
    /// `b`) — a fix that only handled the leaf constructor would miss the middle one. TWO
    /// independent arm-(b) violations fire together on C++, one per level.
    /// </summary>
    [Test]
    public void E08_ThreeLevelsOfInheritance_EachWithItsOwnPostCallWrite()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(E08_ThreeLevels)), Is.EqualTo("211 18"), "C#");
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(E08_ThreeLevels)), Is.EqualTo("211 18"), "JavaScript");
            Assert.That(Norm(MsilHarness.RunExpectingSuccess(E08_ThreeLevels)), Is.EqualTo("211 18"), "MSIL");

            var ex = Assert.Throws<CppCapabilityException>(() => BclE2E.CompileToCppOptimized(E08_ThreeLevels));
            Assert.That(ex!.Message, Does.Contain("captures 'a' of 'L1.New'")
                .And.Contain("captures 'b' of 'L2.New'").And.Contain("#140"),
                "C++ — TWO violations, one per level.\n" + ex.Message);
        });
    }

    internal const string E09_GenericDerived = """
        Class Base
            Public F As Func(Of Integer)
            Public Sub New(f As Func(Of Integer))
                Me.F = f
            End Sub
        End Class

        Class GBox(Of T)
            Inherits Base
            Public Item As T
            Public Sub New(p As Integer)
                MyBase.New(Function() p + 1)
                p = p * 2
            End Sub
        End Class

        Sub Main()
            Dim b As New GBox(Of String)(3)
            Console.WriteLine(b.F())
        End Sub
        """;

    /// <summary>E09: a GENERIC derived class. The capture/write analysis reads the class's own
    /// name (`GBox`), not a closed generic instantiation — arm (b). MSIL hits a SEPARATE,
    /// unrelated pre-existing gap: a lambda's environment nested inside a generic class would
    /// itself need to be generic, out of scope for ClosureLowering's first cut.</summary>
    [Test]
    public void E09_GenericDerivedClass_RefusesNamingTheOpenClassNotAClosedInstantiation()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(FourBackends.RunEmittedCSharp(E09_GenericDerived)), Is.EqualTo("7"), "C#");
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(E09_GenericDerived)), Is.EqualTo("7"), "JavaScript");

            var cpp = Assert.Throws<CppCapabilityException>(() => BclE2E.CompileToCppOptimized(E09_GenericDerived));
            Assert.That(cpp!.Message, Does.Contain("captures 'p' of 'GBox.New'").And.Contain("#140"),
                "C++.\n" + cpp.Message);

            var msil = Msil.MsilHarness.Run(E09_GenericDerived);
            Assert.That(msil.Outcome, Is.EqualTo(Msil.MsilHarness.MsilOutcome.GenerateFailed),
                "MSIL — a SEPARATE pre-existing gap (a lambda's environment inside a generic class " +
                "would itself need to be generic); unaffected by #170.");
            Assert.That(msil.Detail, Does.Contain("generic"), "MSIL.\n" + msil.Detail);
        });
    }

    private const string E11_SharedAndModuleFn = """
        Class Base
            Public N As Integer
            Public Sub New(n As Integer)
                Me.N = n
            End Sub
        End Class

        Function Twice(x As Integer) As Integer
            Return x * 2
        End Function

        Class D
            Inherits Base
            Public Shared Function Sq(x As Integer) As Integer
                Return x * x
            End Function
            Public Sub New(p As Integer)
                MyBase.New(Sq(p) + Twice(p) + D.Sq(1))
            End Sub
        End Class

        Sub Main()
            Dim d As New D(3)
            Console.WriteLine(d.N)
        End Sub
        """;

    /// <summary>E11: a computed argument that CALLS a Shared member (bare and through the class
    /// name) and a module-level function — none of them read the object under construction, so
    /// D4 does not refuse them, and none of them are lambdas, so D3 has nothing to say either.
    /// Runs on every backend.</summary>
    [Test]
    public void E11_SharedAndModuleFunctionCallsInAComputedArgument_RunEverywhere()
        => AssertRunsEverywhereIncludingCpp(E11_SharedAndModuleFn, "16");

    private const string E12_NewListArg = """
        Class Base
            Public L As List(Of Integer)
            Public Sub New(l As List(Of Integer))
                Me.L = l
                Me.L.Add(1)
            End Sub
        End Class

        Class D
            Inherits Base
            Public Sub New()
                MyBase.New(New List(Of Integer)())
                L.Add(2)
            End Sub
        End Class

        Sub Main()
            Dim d As New D()
            Console.WriteLine(d.L.Count)
        End Sub
        """;

    /// <summary><c>New List(Of Integer)()</c> — a REFERENCE-typed constructor call, not a lambda
    /// or a plain value — as the argument. The base constructor mutates the SAME list the derived
    /// constructor later mutates again (proving the argument is not, say, defensively copied).
    /// Runs everywhere.</summary>
    [Test]
    public void E12_ANewListConstructorCallAsTheArgument_RunsEverywhere()
        => AssertRunsEverywhereIncludingCpp(E12_NewListArg, "2");

    private const string E13_ThrowingArg = """
        Class Base
            Public N As Integer
            Public Sub New(n As Integer)
                Me.N = n
                Console.WriteLine("base")
            End Sub
        End Class

        Function Boom(x As Integer) As Integer
            If x > 0 Then
                Throw New Exception("boom " & x)
            End If
            Return x
        End Function

        Class D
            Inherits Base
            Public Sub New(p As Integer)
                MyBase.New(Boom(p))
                Console.WriteLine("derived body")
            End Sub
        End Class

        Sub Main()
            Try
                Dim d As New D(4)
                Console.WriteLine("not reached " & d.N)
            Catch ex As Exception
                Console.WriteLine("caught " & ex.Message)
            End Try
        End Sub
        """;

    /// <summary>A prologue instruction that THROWS before the base call ever runs: neither
    /// <c>Base.New</c>'s own "base" print nor <c>D.New</c>'s "derived body" print reaches
    /// output — the exception propagates out of construction entirely, caught by <c>Main</c>.
    /// Runs everywhere.</summary>
    [Test]
    public void E13_AThrowingPrologueInstruction_PropagatesOutOfConstruction()
        => AssertRunsEverywhereIncludingCpp(E13_ThrowingArg, "caught boom 4");

    // ---- E04/E04b: AndAlso/OrElse in base args (C1's multi-block prologue) -------------------

    internal const string E04_AndAlsoArg = """
        Class Base
            Public BV As Boolean
            Public Sub New(v As Boolean)
                BV = v
            End Sub
        End Class

        Class D
            Inherits Base
            Public W As Integer = 3
            Public Sub New(a As Integer, b As Integer)
                MyBase.New(a > 0 AndAlso b > 0)
                W = W + a
            End Sub
        End Class

        Sub Main()
            Dim d1 As New D(2, 5)
            Dim d2 As New D(2, -5)
            Console.WriteLine(d1.BV & " " & d2.BV & " " & d1.W)
        End Sub
        """;

    internal const string E04b_OrElseArg = """
        Class Base
            Public BV As Boolean
            Public N As Integer
            Public Sub New(v As Boolean, n As Integer)
                BV = v
                N = n
            End Sub
        End Class

        Class D
            Inherits Base
            Public Sub New(a As Integer, b As Integer)
                MyBase.New(a > 0 OrElse b > 0, a + b)
            End Sub
        End Class

        Sub Main()
            Dim d1 As New D(-2, 5)
            Dim d2 As New D(-2, -5)
            Console.WriteLine(d1.BV & " " & d2.BV & " " & d1.N)
        End Sub
        """;

    /// <summary>
    /// E04/E04b: <c>AndAlso</c>/<c>OrElse</c> lower to a MULTI-BLOCK prologue (a local set in
    /// TWO blocks, one per branch) — the orchestrator's clarification C1: JavaScript, MSIL and
    /// C++ all emit a multi-block region; C# alone refuses it by name (<c>ForeignFeatureException</c>,
    /// naming AndAlso/OrElse and ADR-0016 D1). Neither shape regresses anything: `If(c, x, y)`
    /// does not parse in BasicLang at all, so AndAlso/OrElse were the ONLY multi-block prologues
    /// possible before #170 too, and they were refused/broken on every backend then (C++: E11's
    /// straight-line-prefix placement rule; MSIL: "COMPUTED"; JavaScript: a TDZ ReferenceError;
    /// C#: CS0103).
    /// </summary>
    [TestCase(nameof(E04_AndAlsoArg), "True False 5")]
    [TestCase(nameof(E04b_OrElseArg), "True False 0")]
    public void E04_AndAlsoOrElseInABaseArgument_RunsExceptOnCSharp(string which, string expected)
    {
        var source = which == nameof(E04_AndAlsoArg) ? E04_AndAlsoArg : E04b_OrElseArg;
        Assert.Multiple(() =>
        {
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(source)), Is.EqualTo(expected), "JavaScript");
            Assert.That(Norm(MsilHarness.RunExpectingSuccess(source)), Is.EqualTo(expected), "MSIL");
            Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(source))), Is.EqualTo(expected), "C++");

            var ex = Assert.Throws<ForeignFeatureException>(() => FourBackends.RunEmittedCSharp(source));
            Assert.That(ex!.Message, Does.Contain("AndAlso").And.Contain("ADR-0016 D1"),
                "C# must refuse by name, citing AndAlso/OrElse and ADR-0016 D1.\n" + ex.Message);
        });
    }

    // ---- Array literal: C++ runs (E16, ADR-0015 E11's placement); C# refuses by name ---------

    internal const string ArrayLiteralArg = """
        Class Base
            Public Total As Integer
            Public Sub New(xs As Integer())
                For Each x As Integer In xs
                    Total = Total + x
                Next
            End Sub
        End Class
        Class Derived
            Inherits Base
            Public Sub New(a As Integer)
                MyBase.New(New Integer() {a, 2, 3})
            End Sub
        End Class
        Sub Main()
            Dim d As New Derived(1)
            Console.WriteLine(d.Total)
        End Sub
        """;

    /// <summary>
    /// An ARRAY LITERAL <c>MyBase.New</c> argument has no EXPRESSION form on C# (it emits
    /// <c>new int[3]</c> plus element stores — statements — and D1 admits only expressions into
    /// <c>: base(...)</c>), so C# refuses it by name: it was CS0103 (<c>t0</c>) before #170, so
    /// this is not a regression. JavaScript and MSIL emit it as prologue statements before
    /// <c>super(...)</c> / the base <c>call</c>, same as any other prologue. C++ already ran this
    /// shape correctly before #170 (ADR-0015 E11 placed the allocation and element stores at the
    /// call site) — <see cref="CppMeAsValueTests.E16_AnArrayLiteralMyBaseNewArgument_PlacesItsElementStoresBeforeTheBaseCall"/>
    /// is that pin; not repeated here.
    /// </summary>
    [Test]
    public void ArrayLiteralBaseArgument_RunsOnJavaScriptAndMsil_RefusedByNameOnCSharp()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Norm(JavaScriptExecutionTests.RunJs(ArrayLiteralArg)), Is.EqualTo("6"), "JavaScript");
            Assert.That(Norm(MsilHarness.RunExpectingSuccess(ArrayLiteralArg)), Is.EqualTo("6"), "MSIL");

            var ex = Assert.Throws<ForeignFeatureException>(() => FourBackends.RunEmittedCSharp(ArrayLiteralArg));
            Assert.That(ex!.Message, Does.Contain("IRArrayAlloc").Or.Contain("array"),
                "C# must refuse by name, naming the array-literal shape.\n" + ex.Message);
        });
    }

    // ---- E10: the D2a foreign-rooted hierarchy with a PURE COMPUTED argument -------------------

    private const string E10_ForeignPureComputed = """
        #CppInclude "gfoo2.h"
        Using System
        Class D
            Inherits GFoo2
            Public V As Integer = 5
            Public Sub New(a As Integer)
                MyBase.New(a * 2)
                V = V + a
            End Sub
        End Class
        Sub Main()
            Dim d As New D(4)
            Console.WriteLine(d.V)
        End Sub
        """;

    private const string GFoo2Header =
        "#pragma once\nclass GFoo2 { public: int x; GFoo2(int a) : x(a) {} int Get() { return x; } };\n";

    /// <summary>
    /// ADR-0015 D2a (a hierarchy rooted in a <c>#CppInclude</c>d foreign class carries a PURITY rule
    /// on <c>MyBase.New</c> arguments) reads the PROLOGUE now (ADR-0016 C5: "the argument list can be
    /// stale" is moot). <c>a * 2</c> is a pure computed argument into a foreign base whose
    /// constructor takes it: it must still be accepted and run — <c>V = 5 + 4 = 9</c>.
    ///
    /// <para>⛔ Goes through the REAL CLI, not <c>BclE2E</c>: the in-process helper never runs the
    /// Preprocessor, so a <c>#CppInclude</c> line never reaches the generated program
    /// (<c>CppMeAsValueForeignBaseTests</c>' own header; HANDOFF). Only C++ can run it — the other
    /// backends refuse <c>#CppInclude</c> by name.</para>
    /// </summary>
    [Test]
    public void E10_AForeignRootedPureComputedArgument_RunsThroughTheRealCli()
    {
        var compiler = CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var dir = Path.Combine(Path.GetTempPath(), "bl-t170-e10-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Prog.bas"), E10_ForeignPureComputed);
            File.WriteAllText(Path.Combine(dir, "gfoo2.h"), GFoo2Header);

            var (exit, stdout, stderr) = CliTestHarness.RunProcess(
                CliTestHarness.CliPath(), new[] { "Prog.bas", "--target=cpp", "--optimize" }, dir, timeoutMs: 120_000);
            Assert.That(exit, Is.EqualTo(0), $"CLI failed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

            var cppPath = Path.Combine(dir, "Prog.cpp");
            Assert.That(File.Exists(cppPath), Is.True, $"CLI wrote no Prog.cpp.\nSTDOUT:\n{stdout}");

            var headers = new Dictionary<string, string> { ["gfoo2.h"] = GFoo2Header };
            Assert.That(Norm(CppCompile.CompileAndRun(File.ReadAllText(cppPath), compiler.Value, headers)),
                Is.EqualTo("9"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ } }
    }

    // ---- E16/E17: "case a" — the prologue captures NOTHING; a BODY lambda captures its parameter ----

    internal const string E16_CaseAComputed = """
        Class Base
            Public N As Integer
            Public Sub New(n As Integer)
                Me.N = n
            End Sub
        End Class

        Class D
            Inherits Base
            Public A As Integer
            Public Sub New(p As Integer)
                MyBase.New(p * 2)
                Dim bump As Action = Sub() p = p + 1
                bump()
                bump()
                A = p
            End Sub
        End Class

        Sub Main()
            Dim d As New D(5)
            Console.WriteLine(d.N & " " & d.A)
        End Sub
        """;

    internal const string E17_CaseASimple = """
        Class Base
            Public N As Integer
            Public Sub New(n As Integer)
                Me.N = n
            End Sub
        End Class

        Class D
            Inherits Base
            Public A As Integer
            Public Sub New(p As Integer)
                MyBase.New(p)
                Dim bump As Action = Sub() p = p + 1
                bump()
                A = p
            End Sub
        End Class

        Sub Main()
            Dim d As New D(5)
            Console.WriteLine(d.N & " " & d.A)
        End Sub
        """;

    /// <summary>
    /// ⭐ D3's OTHER MSIL placement ("case a"): the prologue creates NO lambda, so ClosureLowering puts the
    /// WHOLE environment right after the base call — where MSIL always wrote it — and the prologue must read
    /// the parameters THEMSELVES (through synthetic copies the rewrite leaves alone), because a BODY lambda
    /// that writes <c>p</c> has hoisted it into an environment that does not exist yet. `p * 2` is computed
    /// before the base call from the raw parameter (10), the body lambda then bumps `p` twice (7). If the
    /// prologue read `p` from the environment it would dereference a null environment: kills mutant M10.
    /// C++ refuses by name (arm (a): `bump` writes `p`).
    /// </summary>
    [Test]
    public void E16_AComputedArgument_ThenABodyLambdaWritesTheParameter_MsilPlacesTheEnvironmentAfterTheCall()
        => AssertRunsEverywhereExceptCpp(E16_CaseAComputed, "10 7", "p", "D.New");

    /// <summary>E17: the same "case a" with a parameter-only argument — byte-identical to pre-#170 on every
    /// backend (ADR-0016 byte identity for a `MyBase.New` whose arguments are parameters and literals).</summary>
    [Test]
    public void E17_AParameterArgument_ThenABodyLambdaWritesTheParameter_RunsAndRefusesLikeE16()
        => AssertRunsEverywhereExceptCpp(E17_CaseASimple, "5 6", "p", "D.New");

    // ---- E14/E15: a BODY lambda after the base call still sees a non-null Me (ADR-0010 D6) -----

    /// <summary>
    /// E14: a base-args lambda that WRITES <c>p</c>, and a constructor-BODY lambda that reads
    /// <c>A</c> (i.e. <c>Me.A</c>) and <c>p</c>: 5 + 10 (the caller's write) + 2 (the base-args
    /// lambda's write, run by <c>d.F()</c>) = 17. The ADR-0010 D6 regression check the ruling asks
    /// for ("a body lambda created after the base call still sees a non-null Me"): on MSIL the
    /// environment's <c>__me</c> is stored immediately AFTER the base call
    /// (<see cref="VisualGameStudio.Tests.Msil.MsilBaseConstructorOrderingTests"/> reads the IL); here
    /// it must actually WORK. C++ refuses by name (arm (a): the base-args lambda writes `p`).
    /// </summary>
    [Test]
    public void E14_ABodyLambdaAfterTheBaseCall_SeesANonNullMe()
        => AssertRunsEverywhereExceptCpp(Msil.MsilBaseConstructorOrderingTests.E14_BodyLambdaUsesMe, "17", "p", "D.New");

    /// <summary>E15: the READ-ONLY sibling — a base-args lambda that only reads <c>p</c> — so nothing
    /// is refused anywhere: all four backends print <c>20 5</c>, including C++.</summary>
    [Test]
    public void E15_AReadOnlyBaseArgsLambdaWithAMeBodyLambda_RunsOnEveryBackend()
        => AssertRunsEverywhereIncludingCpp(Msil.MsilBaseConstructorOrderingTests.E15_ReadOnlyLambdaWithMeBody, "20 5");

    // ============================================================================================
    // The REAL CLI binary (CLAUDE.md: "Validate codegen through the CLI *and* the IR optimizer") —
    // plain and --optimize — for the ruling's own corpus. Every other test in this class drives the
    // in-process helpers or CompileProjectFiles; this one spawns `BasicLang <file> --target=...`,
    // reads the file it wrote, and runs THAT.
    // ============================================================================================

    private static (int Exit, string Output, string Console) CliCompile(string source, string backend, bool optimize)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t170-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Prog.bas"), source);
            var args = new List<string> { "Prog.bas", "--target=" + backend };
            if (optimize) args.Add("--optimize");
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), args.ToArray(), dir, timeoutMs: 120_000);
            var extension = backend switch { "csharp" => ".cs", "javascript" => ".js", "msil" => ".il", "cpp" => ".cpp", _ => throw new ArgumentException(backend) };
            var path = Path.Combine(dir, "Prog" + extension);
            return (exit, File.Exists(path) ? File.ReadAllText(path) : null, stdout + stderr);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    private static string RunViaCli(string backend, string source, bool optimize)
    {
        var (exit, output, console) = CliCompile(source, backend, optimize);
        Assert.That(exit, Is.EqualTo(0), $"CLI --target={backend}{(optimize ? " --optimize" : "")} failed:\n{console}");
        Assert.That(output, Is.Not.Null, "the CLI wrote no output file");
        return backend switch
        {
            "csharp" => FourBackends.RunEmittedCSharpText(output),
            "javascript" => JavaScriptExecutionTests.RunNodeScript(output),
            "msil" => MsilHarness.RunIlExpectingSuccess(output, "T"),
            "cpp" => BclE2E.CompileRun(output),
            _ => throw new ArgumentException(backend),
        };
    }

    /// <summary>(name, source, VB's answer, the captured variable C++ must refuse by name — or null
    /// when C++ runs it too).</summary>
    private static IEnumerable<TestCaseData> CliProbes()
    {
        yield return new TestCaseData(B1, "11 12", "p").SetName("Cli_B1");
        yield return new TestCaseData(B2, "12", "p").SetName("Cli_B2");
        yield return new TestCaseData(B3, "6\n18", "p").SetName("Cli_B3");
        yield return new TestCaseData(B4, "6", "p").SetName("Cli_B4");
        yield return new TestCaseData(B5, "7", null).SetName("Cli_B5");
        yield return new TestCaseData(W1, "2", null).SetName("Cli_W1");
        yield return new TestCaseData(W2, "2", null).SetName("Cli_W2");
        yield return new TestCaseData(C1, "11 12", "p").SetName("Cli_C1");
    }

    /// <summary>
    /// Plain AND <c>--optimize</c>, C#, JavaScript and MSIL through the real CLI print VB's answer;
    /// C++ through the real CLI either runs it (B5, W1, W2) or exits 1 naming the variable, its
    /// creator and #140 (arm (a) for B1/B3/B4/C1, arm (b) for B2).
    /// </summary>
    [TestCaseSource(nameof(CliProbes))]
    public void TheRealCli_PlainAndOptimized_MatchesTheInProcessPaths(string source, string expected, string cppVariable)
    {
        Assert.Multiple(() =>
        {
            foreach (var optimize in new[] { false, true })
            {
                var mode = optimize ? "--optimize" : "plain";
                Assert.That(Norm(RunViaCli("csharp", source, optimize)), Is.EqualTo(expected), $"C# CLI {mode}");
                Assert.That(Norm(RunViaCli("javascript", source, optimize)), Is.EqualTo(expected), $"JavaScript CLI {mode}");
                Assert.That(Norm(RunViaCli("msil", source, optimize)), Is.EqualTo(expected), $"MSIL CLI {mode}");

                if (cppVariable == null)
                    Assert.That(Norm(RunViaCli("cpp", source, optimize)), Is.EqualTo(expected), $"C++ CLI {mode}");
                else
                {
                    var (exit, _, console) = CliCompile(source, "cpp", optimize);
                    Assert.That(exit, Is.Not.EqualTo(0), $"C++ CLI {mode} must refuse");
                    Assert.That(console, Does.Contain($"captures '{cppVariable}' of 'D.New'").And.Contain("#140"), $"C++ CLI {mode}:\n{console}");
                }
            }
        });
    }

    // ---- E09a: a GENERIC BASE class does not parse at all -----------------------------------

    /// <summary>
    /// ⚠ KNOWN LANGUAGE GAP, pinned as current behaviour. E09 above is a generic DERIVED class; a
    /// generic BASE (<c>Inherits Box(Of Integer)</c>) does not PARSE in BasicLang, on any backend, before
    /// or after #170 (<c>Unexpected token in class: '('</c> at the type argument list), so no
    /// <c>MyBase.New</c> shape with a generic base can be probed. When <c>Inherits Box(Of T)</c> parses, add
    /// this program to the four-backend corpus and delete this pin.
    /// </summary>
    [Test]
    public void E09a_AGenericBaseClass_IsAParseGap_Pinned()
    {
        var parser = new Parser(new Lexer("""
            Class Box(Of T)
                Public Get1 As Func(Of T)
                Public Sub New(g As Func(Of T))
                    Get1 = g
                End Sub
            End Class

            Class IntBox
                Inherits Box(Of Integer)
                Public Sub New(p As Integer)
                    MyBase.New(Function() p + 1)
                    p = p * 2
                End Sub
            End Class

            Sub Main()
                Dim b As New IntBox(3)
                Console.WriteLine(b.Get1())
            End Sub
            """).Tokenize());
        parser.Parse();

        Assert.That(parser.Errors.Select(e => e.Message), Has.Some.Contains("Unexpected token in class"),
            "KNOWN GAP: `Inherits Box(Of Integer)` does not parse. If it does now, promote this to a four-backend probe.");
    }
}
