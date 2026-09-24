using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Tests.Compiler;
using static VisualGameStudio.Tests.Msil.MsilHarness;

namespace VisualGameStudio.Tests.Msil;

/// <summary>
/// Pins ADR-0004 D4 — MSIL converts each arithmetic/bitwise operand to the IR result type
/// before the opcode, and each comparison operand to the wider operand kind
/// (<c>docs/superpowers/decisions/0004-family-111-rulings.md</c>). Before this fix, IL
/// arithmetic ran on whatever the operands happened to be — <c>add</c>/<c>mul</c>/<c>rem</c>
/// over a mismatched int32/float64 pair is not a conversion in IL, it is UNDEFINED and the CLR
/// does not verify it. Measured pre-fix garbage this fixture guards against: Integer+Double
/// printed <c>4.4E-323</c>, <c>2 * &lt;Double call&gt;</c> printed <c>1E-323</c>, Single+Integer
/// printed <c>32775</c>, and Single-first <c>7 &gt; 2</c> answered <c>False</c>.
///
/// <para><b>Every operand comes from a Function call</b>, never a literal assigned straight into
/// the arithmetic — a literal-only shape lets constant folding compute the whole expression at
/// compile time and never reach <c>MSILBackend</c>'s binary-op emitter at all, which would pin
/// the optimizer instead of the backend. This mirrors the measurement corpus this fixture is
/// built from (<c>scratchpad/f111/mix/X**{A,Q,R}.bas</c>, <c>scratchpad/f111/frac/F*.bas</c>).</para>
///
/// <para><b>What is deliberately NOT pinned.</b> <c>\</c> (IntDiv) with a floating operand is an
/// OPEN QUESTION (task #127): MSIL rounds the float operand VB-style (round-half-to-even) before
/// the integer <c>div</c>, C++ and JavaScript truncate, and C#'s <c>\</c> is float division
/// entirely — four backends, three different answers, and nobody has ruled which is "right".
/// <see cref="FloatingIntDiv_NoLongerCrashes_ValueIsOpenQuestion_Task127"/> pins only that these
/// shapes no longer throw <c>InvalidProgramException</c> (measured before this fix), never a
/// value. Likewise this file does not pin C#'s own <c>\</c> defect (float division, not IntDiv),
/// C++'s <c>CStr</c>/<c>fmod</c>/<c>Decimal</c> gaps, or JavaScript's absent float32 — those are
/// each a different backend's own defect, out of scope for an MSIL coercion fix.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class MsilBinaryOperandCoercionTests
{
    // ====================================================================================
    // Program builders — every operand crosses a Function-call boundary (see class doc).
    // ====================================================================================

    private static string ArithmeticAndComparisonProgram(string typeA, string litA, string typeB, string litB) =>
        $$"""
        Function VA() As {{typeA}}
            Dim r As {{typeA}} = {{litA}}
            Return r
        End Function

        Function VB() As {{typeB}}
            Dim r As {{typeB}} = {{litB}}
            Return r
        End Function

        Sub Main()
            Dim a As {{typeA}} = VA()
            Dim b As {{typeB}} = VB()
            Console.WriteLine("add=" & CStr(a + b))
            Console.WriteLine("sub=" & CStr(a - b))
            Console.WriteLine("mul=" & CStr(a * b))
            Console.WriteLine("div=" & CStr(a / b))
            If a > b Then
                Console.WriteLine("gt")
            Else
                Console.WriteLine("ngt")
            End If
            If a < b Then
                Console.WriteLine("lt")
            Else
                Console.WriteLine("nlt")
            End If
            If a = b Then
                Console.WriteLine("eq")
            Else
                Console.WriteLine("neq")
            End If
        End Sub
        """;

    private const string ArithmeticAndComparisonExpected =
        "add=9\nsub=5\nmul=14\ndiv=3.5\ngt\nnlt\nneq\n";

    private static string ModProgram(string typeA, string litA, string typeB, string litB) =>
        $$"""
        Function VA() As {{typeA}}
            Dim r As {{typeA}} = {{litA}}
            Return r
        End Function

        Function VB() As {{typeB}}
            Dim r As {{typeB}} = {{litB}}
            Return r
        End Function

        Sub Main()
            Dim a As {{typeA}} = VA()
            Dim b As {{typeB}} = VB()
            Console.WriteLine(CStr(a Mod b))
        End Sub
        """;

    private static string IntDivProgram(string typeA, string litA, string typeB, string litB) =>
        $$"""
        Function VA() As {{typeA}}
            Dim r As {{typeA}} = {{litA}}
            Return r
        End Function

        Function VB() As {{typeB}}
            Dim r As {{typeB}} = {{litB}}
            Return r
        End Function

        Sub Main()
            Dim a As {{typeA}} = VA()
            Dim b As {{typeB}} = VB()
            Console.WriteLine(CStr(a \ b))
        End Sub
        """;

    // ====================================================================================
    // STANDARD pipeline — mixed Integer/Long x Single/Double, + - * and comparisons.
    // Pre-fix these printed denormal garbage (4.4E-323-style) or InvalidProgramException.
    // ====================================================================================

    [TestCase("Integer", "7", "Double", "2")]
    [TestCase("Double", "7", "Integer", "2")]
    [TestCase("Integer", "7", "Single", "2")]
    [TestCase("Single", "7", "Integer", "2")]
    [TestCase("Long", "7", "Double", "2")]
    [TestCase("Double", "7", "Long", "2")]
    [TestCase("Long", "7", "Single", "2")]
    [TestCase("Single", "7", "Long", "2")]
    public void MixedIntegerLongTimesSingleDouble_ArithmeticAndComparisons_MatchLiteralAnswers(
        string typeA, string litA, string typeB, string litB)
    {
        Assert.That(RunExpectingSuccess(ArithmeticAndComparisonProgram(typeA, litA, typeB, litB)),
            Is.EqualTo(ArithmeticAndComparisonExpected));
    }

    [TestCase("Integer", "7", "Double", "2")]
    [TestCase("Double", "7", "Integer", "2")]
    [TestCase("Integer", "7", "Single", "2")]
    [TestCase("Single", "7", "Integer", "2")]
    [TestCase("Long", "7", "Double", "2")]
    [TestCase("Double", "7", "Long", "2")]
    [TestCase("Long", "7", "Single", "2")]
    [TestCase("Single", "7", "Long", "2")]
    public void MixedIntegerLongTimesSingleDouble_Mod_MatchesLiteralAnswer(
        string typeA, string litA, string typeB, string litB)
    {
        Assert.That(RunExpectingSuccess(ModProgram(typeA, litA, typeB, litB)), Is.EqualTo("1\n"));
    }

    /// <summary>
    /// The specific regression named in the ADR brief: Single-first beat Integer, and
    /// <c>7 &gt; 2</c> answered <c>False</c> pre-fix because the compare ran <c>cgt</c> over an
    /// unconverted float32/int32 pair. Both operand orders are asserted so a fix that only
    /// special-cases "Single on the left" cannot pass by accident.
    /// </summary>
    [Test]
    public void SingleFirstVsIntegerFirst_GreaterThan_Answers7GreaterThan2AsTrue()
    {
        Assert.That(
            RunExpectingSuccess(ArithmeticAndComparisonProgram("Single", "7", "Integer", "2")),
            Does.Contain("gt\n").And.Not.Contain("ngt\n"),
            "Single(7) > Integer(2) must answer True");

        Assert.That(
            RunExpectingSuccess(ArithmeticAndComparisonProgram("Integer", "7", "Single", "2")),
            Does.Contain("gt\n").And.Not.Contain("ngt\n"),
            "Integer(7) > Single(2) must answer True");
    }

    /// <summary>
    /// D4's contract text verbatim: <c>2 * &lt;Double call&gt;</c> on the STANDARD pipeline
    /// (pre-fix: <c>1E-323</c>, an <c>i4</c>/<c>r8</c> <c>mul</c> reinterpreted as a denormal
    /// double). <see cref="MsilHarness.Run"/>'s default <c>aggressive: false</c> is the
    /// standard pipeline — this must NOT go through <see cref="RunAggressiveExpectingSuccess"/>.
    /// </summary>
    [Test]
    public void TwoTimesADoubleCall_OnTheStandardPipeline_PrintsTheRoundedProduct()
    {
        Assert.That(RunExpectingSuccess("""
            Function Tag() As Double
                Console.WriteLine("tag")
                Return 1.5
            End Function

            Sub Main()
                Dim r As Double = 2 * Tag()
                Console.WriteLine(r)
            End Sub
            """), Is.EqualTo("tag\n3\n"));
    }

    // ====================================================================================
    // frac/ corpus — specific literal answers named in the brief.
    // ====================================================================================

    [Test]
    public void DoubleMod_7Point5ModInteger2_Equals1Point5()
    {
        // F3: Mod's result type is the WIDER operand (Double), so the Integer operand converts
        // UP, not the Double operand down — unlike IntDiv, whose result is integral.
        Assert.That(RunExpectingSuccess(ModProgram("Double", "7.5", "Integer", "2")),
            Is.EqualTo("1.5\n"));
    }

    [Test]
    public void IntegerTimesSingle_7TimesCSng0Point1_Equals0Point7()
    {
        // F5. Measured against the running program, not scratchpad/f111/frac/F5.exp — that
        // recorded file's "0.70000005" is a STALE expectation for a separate, unrelated CStr
        // Single-formatting question; the actually-emitted value (matrix-step10, frac-s10.txt)
        // and the one the brief calls out is "0.7".
        Assert.That(RunExpectingSuccess("""
            Function VA() As Integer
                Dim r As Integer = 7
                Return r
            End Function

            Function VB() As Single
                Dim r As Single = 0.1
                Return r
            End Function

            Sub Main()
                Dim a As Integer = VA()
                Dim b As Single = VB()
                Console.WriteLine(CStr(a * b))
            End Sub
            """), Is.EqualTo("0.7\n"));
    }

    [Test]
    public void LongTimesDouble_7e9TimesPoint5_Equals3500000000()
    {
        // F7: a Long wide enough that silent double truncation would have been invisible in a
        // small-number test — 7e9 * 0.5 must still land on the exact integer 3500000000.
        Assert.That(RunExpectingSuccess("""
            Function VA() As Long
                Dim r As Long = 7000000000
                Return r
            End Function

            Function VB() As Double
                Dim r As Double = 0.5
                Return r
            End Function

            Sub Main()
                Dim a As Long = VA()
                Dim b As Double = VB()
                Console.WriteLine(CStr(a * b))
            End Sub
            """), Is.EqualTo("3500000000\n"));
    }

    [Test]
    public void LongPlusDouble_UsesFullDoublePrecision_NotSingle()
    {
        // 10000000019 needs ~34 bits — well past float32's 24-bit mantissa but comfortably
        // inside float64's 53-bit one, so a correct conv.r8 keeps the Long EXACT while a
        // conv.r4 (this operand's target is r8, not r4) would silently round it to the nearest
        // float32 first (10000000000, verified against .NET's own float32 rounding). 7e9 in the
        // sibling test above happens to be exactly float32-representable (7e9 = 2^9 * 13671875,
        // and 13671875 fits a 24-bit mantissa) and so cannot tell conv.r4 from conv.r8 apart —
        // this is the shape that can.
        Assert.That(RunExpectingSuccess("""
            Function VA() As Long
                Dim r As Long = 10000000019
                Return r
            End Function

            Function VB() As Double
                Dim r As Double = 1.5
                Return r
            End Function

            Sub Main()
                Dim a As Long = VA()
                Dim b As Double = VB()
                Console.WriteLine(CStr(a + b))
            End Sub
            """), Is.EqualTo("10000000020.5\n"));
    }

    [Test]
    public void SingleVsDoubleEqualLiteralValue_ComparesUnequal_ButSingleIsGreater()
    {
        // FC: 0.1f widened to double is NOT 0.1d — Eq must answer false (comparing at r8, the
        // wider kind) while Gt still answers true, both matching real .NET Single/Double math.
        Assert.That(RunExpectingSuccess("""
            Function VA() As Single
                Dim r As Single = 0.1
                Return r
            End Function

            Function VB() As Double
                Dim r As Double = 0.1
                Return r
            End Function

            Sub Main()
                Dim a As Single = VA()
                Dim b As Double = VB()
                If a = b Then
                    Console.WriteLine("eq")
                Else
                    Console.WriteLine("neq")
                End If
                If a > b Then
                    Console.WriteLine("gt")
                Else
                    Console.WriteLine("ngt")
                End If
            End Sub
            """), Is.EqualTo("neq\ngt\n"));
    }

    // ====================================================================================
    // ⛔ OPEN QUESTION (task #127) — `\` with a floating operand. Pin "does not crash" only.
    // ====================================================================================

    /// <summary>
    /// Before this fix these threw <c>InvalidProgramException</c> for at least some shapes (an
    /// unconverted int32/float64 pair feeding an integer <c>div</c> opcode is exactly the
    /// "undefined in IL" case the class doc describes). After this fix they run to completion.
    /// The PRINTED VALUE is deliberately not asserted — VB-style rounding (MSIL), truncation
    /// (C++/JavaScript) and float division (C#) disagree, and task #127 is still open on which
    /// is correct. If this ever needs a value pinned, resolve #127 first.
    /// </summary>
    [TestCase("Double", "7.5", "Integer", "2", TestName = "FloatingIntDiv_NoLongerCrashes_DoubleByInteger")]
    [TestCase("Double", "6.5", "Integer", "2", TestName = "FloatingIntDiv_NoLongerCrashes_DoubleByInteger_HalfToEven")]
    [TestCase("Integer", "7", "Double", "2.5", TestName = "FloatingIntDiv_NoLongerCrashes_IntegerByDouble")]
    [TestCase("Single", "2.5", "Integer", "2", TestName = "FloatingIntDiv_NoLongerCrashes_SingleByInteger")]
    public void FloatingIntDiv_NoLongerCrashes_ValueIsOpenQuestion_Task127(
        string typeA, string litA, string typeB, string litB)
    {
        var result = Run(IntDivProgram(typeA, litA, typeB, litB));
        Assert.That(result.Outcome, Is.EqualTo(MsilOutcome.Ran), result.Report);
        // Deliberately no assertion on result.Output — see the method doc.
    }

    // ====================================================================================
    // AGGRESSIVE pipeline — the same regressions, through AddAggressivePasses().
    // ====================================================================================

    [TestCase("Integer", "7", "Double", "2")]
    [TestCase("Single", "7", "Integer", "2")]
    [TestCase("Long", "7", "Double", "2")]
    public void MixedArithmeticAndComparisons_OnTheAggressivePipeline_MatchLiteralAnswers(
        string typeA, string litA, string typeB, string litB)
    {
        Assert.That(RunAggressiveExpectingSuccess(ArithmeticAndComparisonProgram(typeA, litA, typeB, litB)),
            Is.EqualTo(ArithmeticAndComparisonExpected));
    }

    [Test]
    public void DoubleMod_OnTheAggressivePipeline_Equals1Point5()
    {
        Assert.That(RunAggressiveExpectingSuccess(ModProgram("Double", "7.5", "Integer", "2")),
            Is.EqualTo("1.5\n"));
    }

    // ====================================================================================
    // Release .blproj — the THIRD entry point: BasicCompiler.CompileProjectFiles under the
    // CLI's own "-c Release" -> OptimizeAggressive mapping (Program.cs:502), a different path
    // through the compiler than MsilHarness.CompileToIl's direct AggressivePipeline.Apply call.
    // The CLI's MSIL build step only EMITS the .il (no ilasm call of its own — Program.cs never
    // shells to ilasm for this target) so ilasm/dotnet still run through MsilHarness.RunIl.
    // ====================================================================================

    private string _projectDir = null!;

    [SetUp]
    public void SetUp()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), "bl-msilcoercion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_projectDir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private string BuildReleaseMsilAndRun(string basSource)
    {
        File.WriteAllText(Path.Combine(_projectDir, "Main.bas"), basSource);
        File.WriteAllText(Path.Combine(_projectDir, "App.blproj"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>MSIL</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Main.bas" />
              </ItemGroup>
            </BasicLangProject>
            """);

        var (buildExit, buildOut, buildErr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(),
            new[] { "build", Path.Combine(_projectDir, "App.blproj"), "-c", "Release" },
            _projectDir,
            timeoutMs: 120_000);
        Assert.That(buildExit, Is.EqualTo(0),
            $"CLI Release MSIL build failed.\nSTDOUT:\n{buildOut}\nSTDERR:\n{buildErr}");

        var ilFiles = Directory.GetFiles(_projectDir, "App.il", SearchOption.AllDirectories);
        Assert.That(ilFiles, Is.Not.Empty,
            $"CLI build claimed success but produced no App.il.\nSTDOUT:\n{buildOut}");

        return RunIlExpectingSuccess(File.ReadAllText(ilFiles[0]), "App");
    }

    [Test]
    public void ReleaseBlprojBuild_IntegerTimesDouble_ArithmeticAndComparisons_MatchLiteralAnswers()
    {
        Assert.That(
            BuildReleaseMsilAndRun(ArithmeticAndComparisonProgram("Integer", "7", "Double", "2")),
            Is.EqualTo(ArithmeticAndComparisonExpected));
    }

    [Test]
    public void ReleaseBlprojBuild_SingleFirstGreaterThanInteger_AnswersTrue()
    {
        Assert.That(
            BuildReleaseMsilAndRun(ArithmeticAndComparisonProgram("Single", "7", "Integer", "2")),
            Does.Contain("gt\n").And.Not.Contain("ngt\n"));
    }

    // ====================================================================================
    // C# as the oracle — where C# is right. NOT a four-backend comparison: C++'s CStr(Double)
    // prints a fixed "9.000000" (six decimals, always) where C#/MSIL print "9", and C++ has no
    // int/Double Mod lowering at all (measured: "invalid operands to binary expression" from
    // clang). Both are pre-existing, backend-local defects unrelated to this fix, so folding
    // them into an exact-string four-way comparison would pin somebody else's bug under this
    // fix's name. C# is the correct, uncontested oracle for ordinary IEEE arithmetic, so this
    // compares MSIL against C# alone — both in-process, through the same seams FourBackends
    // itself uses.
    // ====================================================================================

    [Test]
    public void SingleFirstGreaterThanInteger_AgreesWithTheCSharpOracle()
    {
        var program = ArithmeticAndComparisonProgram("Single", "7", "Integer", "2");
        var csharp = FourBackends.Norm(FourBackends.RunEmittedCSharp(program));
        var msil = FourBackends.Norm(RunExpectingSuccess(program));
        Assert.That(msil, Is.EqualTo(csharp), "MSIL must agree with the C# oracle");
        Assert.That(csharp, Is.EqualTo(ArithmeticAndComparisonExpected.Trim()));
    }

    [Test]
    public void LongTimesDouble_AgreesWithTheCSharpOracle()
    {
        var program = ArithmeticAndComparisonProgram("Long", "7", "Double", "2");
        var csharp = FourBackends.Norm(FourBackends.RunEmittedCSharp(program));
        var msil = FourBackends.Norm(RunExpectingSuccess(program));
        Assert.That(msil, Is.EqualTo(csharp), "MSIL must agree with the C# oracle");
    }

    // ====================================================================================
    // Controls: shapes ADR-0004 D4 says must NOT change. Any of these going red is a
    // regression in the coercion, not evidence of a new bug it should fix.
    // ====================================================================================

    [TestCase("Integer")]
    [TestCase("Double")]
    [TestCase("Single")]
    [TestCase("Long")]
    public void SameTypePairs_ArithmeticAndComparisons_AreUnaffected(string type)
    {
        Assert.That(RunExpectingSuccess(ArithmeticAndComparisonProgram(type, "7", type, "2")),
            Is.EqualTo(ArithmeticAndComparisonExpected));
    }

    [Test]
    public void IntegerLongMix_IntDiv_IsUnaffected()
    {
        // F6: both operands integral (Long \ Integer) — IntDiv's own "already-existing" path,
        // untouched territory relative to the floating-\ open question above.
        Assert.That(RunExpectingSuccess(IntDivProgram("Long", "7000000000", "Integer", "3")),
            Is.EqualTo("2333333333\n"));
    }

    [Test]
    public void DivisionOperator_MixedIntegerDouble_IsUnaffectedByTheCoercion()
    {
        // `/` (Div) already got an explicit IRCast from IRBuilder before this fix — by the time
        // BinaryOperandKind/EmitNumericCoercion see it, both operands already agree, so the
        // "kinds already agree -> no-op" early-out is what keeps this a single conv.r8 instead
        // of a double conversion. See the mutation report for what removing that early-out does.
        Assert.That(RunExpectingSuccess("""
            Function VA() As Integer
                Dim r As Integer = 7
                Return r
            End Function

            Function VB() As Double
                Dim r As Double = 2
                Return r
            End Function

            Sub Main()
                Dim a As Integer = VA()
                Dim b As Double = VB()
                Console.WriteLine(CStr(a / b))
            End Sub
            """), Is.EqualTo("3.5\n"));
    }

    [Test]
    public void StringConcat_OfMixedNumericOperands_IsUnaffected()
    {
        // Concat is excluded from BinaryOperandKind entirely (falls to its `default: return
        // null` arm) — EmitNumericCoercion never runs for it. This exercises `&` directly (not
        // through CStr, which is a separate call) over an Integer and a Double operand.
        Assert.That(RunExpectingSuccess("""
            Function VA() As Integer
                Dim r As Integer = 7
                Return r
            End Function

            Function VB() As Double
                Dim r As Double = 2.5
                Return r
            End Function

            Sub Main()
                Dim a As Integer = VA()
                Dim b As Double = VB()
                Console.WriteLine(CStr(a) & "," & CStr(b))
            End Sub
            """), Is.EqualTo("7,2.5\n"));
    }

    // ⛔ NOT WRITABLE: Decimal is already completely non-functional on MSIL, before and after
    // this fix, unrelated to it. Measured directly: even a vanilla same-type
    // `Dim a As Decimal = 2 : Dim b As Decimal = 3 : Dim s As Decimal = a + b` (no mixing, no
    // CStr, no comparison) fails to assemble — `MSILBackend`'s Decimal type-spec
    // ("valuetype [System.Runtime]System.Decimal") is being run through whatever sanitizes an
    // IL label/identifier, which strips the spaces and brackets and then prefixes it with
    // `class`, so ilasm sees `class 'valuetypeSystemRuntimeSystemDecimal'` and rejects it as an
    // undefined class on every single use of the type — the return type, the locals, the
    // parameters. `NumericKind(Decimal)` returning null confirms this diff never touches
    // Decimal at all (it cannot be what broke this), but there is no "still works" shape to pin
    // it against, because it never worked. Reported, not fixed here (see the handback message).

    // ====================================================================================
    // Select Case `When` guard — EmitInlineValue's own copy of the coercion (mutation e).
    // The guard's operands must be locals materialized BEFORE the Select Case: a raw call
    // inside a suppressed-emit guard has no local slot (see EmitInlineValue's fallback arm) and
    // is refused outright, so this cannot be built with call operands the way the rest of this
    // fixture is — that refusal is orthogonal to numeric coercion.
    // ====================================================================================

    [Test]
    public void SelectCaseWhenGuard_MixedIntegerDoubleComparison_TakesTheRightBranch()
    {
        // A guard that is DIRECTLY a comparison (IRCompare) between two locals of different
        // numeric kinds — WiderNumericKind(compare.Left, compare.Right) reads each leaf's own
        // .Type, which locals always carry, so this exercises EmitInlineValue's IRCompare
        // coercion arm cleanly. (A guard built from a NESTED arithmetic expression is a
        // different, currently-broken shape — see the PinnedDivergence test below.)
        Assert.That(RunExpectingSuccess("""
            Function VI() As Integer
                Dim r As Integer = 3
                Return r
            End Function

            Function VD() As Double
                Dim r As Double = 2.5
                Return r
            End Function

            Sub Main()
                Dim a As Integer = VI()
                Dim b As Double = VD()
                Dim x As Integer = 1
                Select Case x
                    Case 1 When a > b
                        Console.WriteLine("yes")
                    Case Else
                        Console.WriteLine("no")
                End Select
            End Sub
            """), Is.EqualTo("yes\n"));
    }

    [Test]
    public void SelectCaseWhenGuard_MixedIntegerDoubleComparison_TakesTheElseBranch()
    {
        // The mirror of the above: Integer(3) is NOT greater than Double(3.5), so a coercion
        // bug that always evaluates true (or corrupts the stack into always-false) cannot pass
        // both this test and the one above.
        Assert.That(RunExpectingSuccess("""
            Function VI() As Integer
                Dim r As Integer = 3
                Return r
            End Function

            Function VD() As Double
                Dim r As Double = 3.5
                Return r
            End Function

            Sub Main()
                Dim a As Integer = VI()
                Dim b As Double = VD()
                Dim x As Integer = 1
                Select Case x
                    Case 1 When a > b
                        Console.WriteLine("yes")
                    Case Else
                        Console.WriteLine("no")
                End Select
            End Sub
            """), Is.EqualTo("no\n"));
    }

    /// <summary>
    /// ⛔ PINNED DIVERGENCE, found while building this fixture, NOT introduced by ADR-0004 D4.
    /// A guard built from a NESTED arithmetic sub-expression (<c>a + b</c>, not a bare
    /// comparison) gets NO coercion at all, even after this fix: <c>BinaryOperandKind</c> reads
    /// <c>binaryOp.Type</c>, and for this specific node that type is not the populated
    /// <c>Double</c> IRBuilder's ordinary <c>Visit(BinaryExpressionNode)</c> path produces —
    /// measured directly (<c>MsilHarness.CompileToIl</c> on the shape below) as
    /// <c>ldloc.0 / ldloc.1 / add</c> with NO <c>conv.r8</c> in between, where the equivalent
    /// non-guard expression correctly emits one. The pre-fix root cause (an unconverted
    /// int32/float64 pair reaching a primitive opcode) is exactly what D4 fixes everywhere else
    /// — this is the one shape the fix's own <c>EmitInlineValue</c> copy cannot reach, because
    /// the coercion has nothing to key off. It does not crash (the CLR's JIT does not verify
    /// this at runtime), so this pins ONLY the outcome, never the printed branch — the branch
    /// taken depends on how the JIT happens to reinterpret the mismatched bit pattern, which is
    /// undefined behavior, not a value this fixture can promise.
    /// </summary>
    [Test]
    public void SelectCaseWhenGuard_NestedMixedArithmetic_PinnedDivergence_NoCoercionApplied()
    {
        var il = MsilHarness.CompileToIl("""
            Function VI() As Integer
                Dim r As Integer = 3
                Return r
            End Function

            Function VD() As Double
                Dim r As Double = 2.5
                Return r
            End Function

            Sub Main()
                Dim a As Integer = VI()
                Dim b As Double = VD()
                Dim x As Integer = 1
                Select Case x
                    Case 1 When a + b > 5.0
                        Console.WriteLine("yes")
                    Case Else
                        Console.WriteLine("no")
                End Select
            End Sub
            """);
        Assert.That(il, Does.Match(@"ldloc\.0\s*\r?\n\s*ldloc\.1\s*\r?\n\s*add"),
            "expected the KNOWN-BROKEN shape (int32 'a' loaded and added with no conv.r8 before "
            + "it) — if this no longer matches, the guard's nested-arithmetic coercion gap has "
            + "been fixed and this pin should be promoted to a real value assertion instead.");
    }
}
